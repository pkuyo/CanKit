using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Virtual;
using CanKit.Core;
using CanKit.Core.Definitions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Frame ownership on the Virtual adapter's broadcast hub: every recipient gets its own copy, the echo
/// is marked, and hubs do not outlive their last member.
/// </summary>
public class VirtualBusOwnershipTests : IClassFixture<TestCaseProvider>
{
    private static string NewSession() => $"ownership-{Guid.NewGuid():N}";

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
        } // the caller releases its own buffer right after Transmit returns

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var recvB = (await consumerB.ReceiveAsync(1, 2000, cts.Token)).Single();
        var recvC = (await consumerC.ReceiveAsync(1, 2000, cts.Token)).Single();

        recvB.CanFrame.Data.ToArray().Should().Equal(payload);
        recvC.CanFrame.Data.ToArray().Should().Equal(payload);

        recvB.CanFrame.Dispose();

        recvC.CanFrame.Data.ToArray().Should().Equal(payload);
        recvC.CanFrame.Dispose();
    }

    [Fact]
    public async Task Echo_Mode_Delivers_A_Marked_Independent_Copy_To_The_Sender()
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

        using (CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate)))
        using (CanBus.Open($"virtual://{session}/1", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate)))
        {
            hubs.Contains(session).Should().BeTrue();
        }

        hubs.Contains(session).Should().BeFalse();
    }

    [Fact]
    public void Hub_Handle_From_Get_Stays_Connected_After_The_Last_Bus_Leaves()
    {
        var session = NewSession();
        var hub = VirtualBusHub.Get(session);

        CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate)).Dispose();
        using var again = CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

        VirtualBusHub.Get(session).Should().BeSameAs(hub);
    }

    [Fact]
    public void Disposing_A_Bus_Releases_Unread_Copies()
    {
        var session = NewSession();
        var allocator = new CountingAllocator();

        using var sender = CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));
        var receiver = CanBus.Open($"virtual://{session}/1", cfg =>
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate).BufferAllocator(allocator));

        sender.Transmit(CanFrame.Classic(0x1, new byte[] { 1 }));
        sender.Transmit(CanFrame.Classic(0x2, new byte[] { 2 }));
        allocator.Rented.Should().Be(2);

        receiver.Dispose();

        allocator.Disposed.Should().Be(2);
    }

    [Fact]
    public void Disposing_The_Bus_From_Its_Own_Handler_Does_Not_Leak_The_Frame()
    {
        var session = NewSession();
        var allocator = new CountingAllocator();

        using var sender = CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));
        var receiver = CanBus.Open($"virtual://{session}/1", cfg =>
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate).BufferAllocator(allocator));
#pragma warning disable CS0618
        receiver.FrameReceived += (_, _) => receiver.Dispose();
#pragma warning restore CS0618

        sender.Transmit(CanFrame.Classic(0x1, new byte[] { 1 }));

        allocator.Rented.Should().Be(1);
        allocator.Disposed.Should().Be(1);
    }

    private sealed class CountingAllocator : CanKit.Abstractions.SPI.Common.IBufferAllocator
    {
        public int Rented, Disposed;
        public bool FrameNeedDispose => true;

        public System.Buffers.IMemoryOwner<byte> Rent(int length, bool zeroFill = false)
        {
            Interlocked.Increment(ref Rented);
            return new Owner(this, new byte[length]);
        }

        private sealed class Owner(CountingAllocator parent, byte[] buf) : System.Buffers.IMemoryOwner<byte>
        {
            public Memory<byte> Memory { get; } = buf;
            public void Dispose() => Interlocked.Increment(ref parent.Disposed);
        }
    }
}
