using System;
using System.Buffers;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Adapter.Virtual;
using CanKit.Core;
using CanKit.Core.Definitions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Verifies the frame ownership contract (docs/architecture/arc42-CanKit.md §8.1,
/// FR-RAW-001..005) for the Virtual adapter's multi-consumer broadcast hub.
/// </summary>
public class VirtualBusOwnershipTests : IClassFixture<TestCaseProvider>
{
    private static string NewSession() => $"ownership-{Guid.NewGuid():N}";

    // Wraps ArrayPoolBufferAllocator to count buffers that are rented but never returned,
    // so leaks (e.g. a delivery that's dropped by a software filter without disposing its
    // RX-lease copy) show up as a non-zero Outstanding count instead of silently exhausting
    // the shared ArrayPool over time.
    private sealed class CountingBufferAllocator : IBufferAllocator
    {
        private readonly ArrayPoolBufferAllocator _inner = new();
        private int _outstanding;

        public int Outstanding => Volatile.Read(ref _outstanding);

        public IMemoryOwner<byte> Rent(int length, bool zeroFill = false)
        {
            Interlocked.Increment(ref _outstanding);
            return new TrackedOwner(_inner.Rent(length, zeroFill), this);
        }

        public bool FrameNeedDispose => true;

        private sealed class TrackedOwner(IMemoryOwner<byte> inner, CountingBufferAllocator owner) : IMemoryOwner<byte>
        {
            private int _disposed;

            public Memory<byte> Memory => inner.Memory;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    inner.Dispose();
                    Interlocked.Decrement(ref owner._outstanding);
                }
            }
        }
    }

    [Fact]
    public async Task Broadcast_Gives_Each_Consumer_An_Independent_Copy_Safe_To_Dispose_Early()
    {
        var session = NewSession();
        var allocator = new ArrayPoolBufferAllocator();

        ICanBus Open(int channel) => CanBus.Open($"virtual://{session}/{channel}", cfg =>
            cfg.SetProtocolMode(CanProtocolMode.Can20)
                .Baud(TestCaseProvider.AbitRate)
                .BufferAllocator(allocator)
                .SetAsyncBufferCapacity(16));

        using var sender = Open(0);
        using var consumerB = Open(1);
        using var consumerC = Open(2);

        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using (var owner = allocator.Rent(payload.Length))
        {
            payload.AsSpan().CopyTo(owner.Memory.Span);
            using var toSend = CanFrame.Classic(0x123, owner, ownMemory: false);
            sender.Transmit(toSend);
        } // caller (TX-lease) disposes its own buffer right after Transmit returns

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var recvB = (await consumerB.ReceiveAsync(1, 2000, cts.Token)).Single();
        var recvC = (await consumerC.ReceiveAsync(1, 2000, cts.Token)).Single();

        recvB.CanFrame.Data.ToArray().Should().Equal(payload);
        recvC.CanFrame.Data.ToArray().Should().Equal(payload);

        // B disposes its own RX-lease copy immediately...
        recvB.CanFrame.Dispose();

        // ...C must still see the untouched payload afterward (FR-RAW-004).
        recvC.CanFrame.Data.ToArray().Should().Equal(payload);
        recvC.CanFrame.Dispose();
    }

    [Fact]
    public async Task Echo_Mode_Delivers_An_Independent_Copy_To_The_Sender()
    {
        var session = NewSession();

        using var sender = CanBus.Open($"virtual://{session}/0", cfg =>
            cfg.SetProtocolMode(CanProtocolMode.Can20)
                .Baud(TestCaseProvider.AbitRate)
                .SetWorkMode(ChannelWorkMode.Echo)
                .SetAsyncBufferCapacity(16));

        var frame = CanFrame.Classic(0x321, new byte[] { 9, 8, 7 });
        sender.Transmit(frame);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var echoed = (await sender.ReceiveAsync(1, 2000, cts.Token)).Single();

        echoed.IsEcho.Should().BeTrue();
        echoed.CanFrame.Data.ToArray().Should().Equal(9, 8, 7);
        echoed.CanFrame.Dispose();
    }

    [Fact]
    public void Hub_Is_Removed_From_Registry_Once_Its_Last_Member_Disposes()
    {
        var session = NewSession();
        var field = typeof(VirtualBusHub).GetField("_hubs", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VirtualBusHub._hubs field not found.");
        var hubs = (IDictionary)field.GetValue(null)!;

        using (var busA = CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate)))
        using (var busB = CanBus.Open($"virtual://{session}/1", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate)))
        {
            hubs.Contains(session).Should().BeTrue();
        }

        hubs.Contains(session).Should().BeFalse();
    }

    [Fact]
    public void Filter_Rejected_Delivery_Releases_Its_Rented_Buffer()
    {
        var session = NewSession();
        var allocator = new CountingBufferAllocator();

        using var sender = CanBus.Open($"virtual://{session}/0", cfg =>
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

        // Only accepts IDs in [0x001, 0x002]; a frame sent on 0x123 will be dropped by the
        // software filter inside InternalDeliver.
        using var rejecting = CanBus.Open($"virtual://{session}/1", cfg =>
            cfg.SetProtocolMode(CanProtocolMode.Can20)
                .Baud(TestCaseProvider.AbitRate)
                .BufferAllocator(allocator)
                .RangeFilter(0x001, 0x002, CanFilterIDType.Standard));

        sender.Transmit(CanFrame.Classic(0x123, new byte[] { 1, 2, 3 }));

        // Delivery (including the filter check) happens synchronously inside Transmit, so no
        // wait is needed before asserting the rejected copy's buffer was released.
        allocator.Outstanding.Should().Be(0);
    }
}
