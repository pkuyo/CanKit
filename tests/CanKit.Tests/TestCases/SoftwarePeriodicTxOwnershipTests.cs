using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Core;
using CanKit.Core.Exceptions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Regression tests for the TX-lease rule (FR-RAW-005, frame ownership contract in
/// docs/architecture/arc42-CanKit.md §8.1) in <c>SoftwarePeriodicTx</c>: the periodic
/// loop must transmit from its own duplicated frame copy, so a caller that disposes an
/// owning frame right after Start/Update cannot poison (use-after-free) the payload the
/// loop keeps emitting. <see cref="VirtualBus.TransmitPeriodic"/> and the
/// Vector/PCAN/Kvaser software fallbacks all route through this path.
/// </summary>
public class SoftwarePeriodicTxOwnershipTests : IClassFixture<TestCaseProvider>
{
    private static string NewSession() => $"sptx-own-{Guid.NewGuid():N}";

    // Allocator whose owners overwrite their buffer with a poison byte on Dispose: any
    // reader of caller-frame memory after the caller disposed it observes poison instead
    // of the original payload, turning a lease violation into a hard test failure.
    private sealed class PoisonOnDisposeAllocator : IBufferAllocator
    {
        private readonly byte _poison;

        public PoisonOnDisposeAllocator(byte poison) => _poison = poison;

        public IMemoryOwner<byte> Rent(int length, bool zeroFill = false) => new Owner(length, _poison);

        public bool FrameNeedDispose => true;

        private sealed class Owner : IMemoryOwner<byte>
        {
            private readonly byte _poison;
            private readonly Memory<byte> _memory;

            public Owner(int length, byte poison)
            {
                _poison = poison;
                _memory = new byte[length];
            }

            public Memory<byte> Memory => _memory;

            public void Dispose() => _memory.Span.Fill(_poison);
        }
    }

    // Counting allocator (same idea as VirtualBusOwnershipTests.CountingBufferAllocator) to
    // assert that the internally duplicated copy is released again on Update/Stop.
    private sealed class CountingAllocator : IBufferAllocator
    {
        private int _outstanding;

        public int Outstanding => Volatile.Read(ref _outstanding);

        public IMemoryOwner<byte> Rent(int length, bool zeroFill = false)
        {
            Interlocked.Increment(ref _outstanding);
            return new Tracked(length, this);
        }

        public bool FrameNeedDispose => true;

        private sealed class Tracked : IMemoryOwner<byte>
        {
            private readonly CountingAllocator _owner;
            private readonly Memory<byte> _memory;
            private int _disposed;

            public Tracked(int length, CountingAllocator owner)
            {
                _owner = owner;
                _memory = new byte[length];
            }

            public Memory<byte> Memory => _memory;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    Interlocked.Decrement(ref _owner._outstanding);
                }
            }
        }
    }

    private static ICanBus Open(string session, int channel, IBufferAllocator allocator) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20)
            .Baud(TestCaseProvider.AbitRate)
            .BufferAllocator(allocator)
            .SoftwareFeaturesFallBack(CanFeature.All)
            .SetAsyncBufferCapacity(32));

    [Fact]
    public async Task Periodic_Loop_Transmits_From_Owned_Copy_After_Caller_Disposes_Owning_Frame()
    {
        var session = NewSession();
        var busAllocator = new CountingAllocator();
        using var sender = Open(session, 0, busAllocator);
        using var receiver = Open(session, 1, new CountingAllocator());

        var payload = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
        var callerAllocator = new PoisonOnDisposeAllocator(0xEE);
        var owner = callerAllocator.Rent(payload.Length);
        payload.AsSpan().CopyTo(owner.Memory.Span);
        var callerFrame = CanFrame.Classic(0x321, owner, ownMemory: true);

        using var periodic = sender.TransmitPeriodic(
            callerFrame,
            new PeriodicTxOptions(TimeSpan.FromMilliseconds(5), repeat: -1, fireImmediately: true));

        // TX-lease: the caller stays owner of the passed frame and disposes it right
        // after the call returned — this poisons the caller-side buffer.
        callerFrame.Dispose();

        // Drain several emissions; every one must still carry the original payload. A
        // loop that kept referencing the caller's (now poisoned) buffer would emit 0xEE.
        for (var i = 0; i < 3; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var received = (await receiver.ReceiveAsync(1, 2000, cts.Token)).Single();
            var frame = received.CanFrame;
            try
            {
                frame.Data.ToArray().Should().Equal(payload,
                    "the periodic loop must transmit from its own duplicated copy (TX-lease, FR-RAW-005)");
            }
            finally
            {
                frame.Dispose();
            }
        }

        periodic.Stop();

        busAllocator.Outstanding.Should().Be(0,
            "Stop must release the internally duplicated frame copy (no leaked rental)");
    }

    [Fact]
    public async Task Update_Replaces_The_Owned_Copy_And_Releases_The_Previous_One()
    {
        var session = NewSession();
        var busAllocator = new CountingAllocator();
        using var sender = Open(session, 0, busAllocator);
        using var receiver = Open(session, 1, new CountingAllocator());

        var payloadA = new byte[] { 0x01 };
        var payloadB = new byte[] { 0x02, 0x03 };

        var allocatorA = new PoisonOnDisposeAllocator(0xAA);
        var ownerA = allocatorA.Rent(payloadA.Length);
        payloadA.AsSpan().CopyTo(ownerA.Memory.Span);
        var frameA = CanFrame.Classic(0x321, ownerA, ownMemory: true);

        using var periodic = sender.TransmitPeriodic(
            frameA,
            new PeriodicTxOptions(TimeSpan.FromMilliseconds(5), repeat: -1, fireImmediately: true));
        frameA.Dispose(); // poison A's caller-side buffer

        var allocatorB = new PoisonOnDisposeAllocator(0xBB);
        var ownerB = allocatorB.Rent(payloadB.Length);
        payloadB.AsSpan().CopyTo(ownerB.Memory.Span);
        var frameB = CanFrame.Classic(0x321, ownerB, ownMemory: true);

        periodic.Update(frame: frameB);
        frameB.Dispose(); // poison B's caller-side buffer

        busAllocator.Outstanding.Should().Be(1,
            "Update must release the previously owned copy when swapping in the new one");

        // Read until the first post-Update emission arrives (one in-flight A frame from a
        // tick concurrent with the Update call is tolerated); it must carry B's payload
        // from the new owned copy, not the poisoned caller buffer.
        byte[]? observed = null;
        for (var attempt = 0; attempt < 5 && observed is null; attempt++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var received = (await receiver.ReceiveAsync(1, 2000, cts.Token)).Single();
            var frame = received.CanFrame;
            try
            {
                if (frame.Data.Length == payloadB.Length)
                {
                    observed = frame.Data.ToArray();
                }
            }
            finally
            {
                frame.Dispose();
            }
        }

        observed.Should().NotBeNull("the updated frame must be emitted after Update");
        observed!.Should().Equal(payloadB,
            "the loop must emit the new owned copy, unaffected by the caller's Dispose");

        periodic.Stop();

        busAllocator.Outstanding.Should().Be(0,
            "Stop must release the currently owned copy (no leaked rental)");
    }

    [Fact]
    public void Update_After_Stop_Throws_And_Does_Not_Leak()
    {
        var busAllocator = new CountingAllocator();
        using var sender = Open(NewSession(), 0, busAllocator);

        using var periodic = sender.TransmitPeriodic(
            CanFrame.Classic(0x321, new byte[] { 0x01 }),
            new PeriodicTxOptions(TimeSpan.FromMilliseconds(10), repeat: -1, fireImmediately: false));

        periodic.Stop();

        Action act = () => periodic.Update(frame: CanFrame.Classic(0x321, new byte[] { 0x02 }));
        act.Should().Throw<CanBusDisposedException>(
            "the object is dead after Stop; silently accepting a new frame would leak its copy");

        busAllocator.Outstanding.Should().Be(0);
    }
}
