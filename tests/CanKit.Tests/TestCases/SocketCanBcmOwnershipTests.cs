using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Core;
using CanKit.Core.Definitions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Focused unit tests for SocketCAN BCM periodic-TX ownership and RemainingCount
/// robustness (arc42 §8.1 / FR-RAW-005; Review §1.4 remaining half).
///
/// These tests self-skip unless the SocketCAN adapter is the target of this
/// CI job (CANKIT_TEST_ADAPTERS=CanKit.Adapter.SocketCAN), matching the
/// self-skip convention documented in AGENTS.md. Under -c Fake the SocketCAN
/// adapter loads its in-memory Libc.Fake backend, which lets us exercise the
/// BCM code paths without vcan or a real kernel.
///
/// Fake caveat: the Fake backend replies to TX_READ synchronously by
/// pre-enqueuing a TX_STATUS onto the query socket, so the read() in
/// RemainingCount does not actually hit EAGAIN under Fake. These tests
/// therefore cannot fully exercise the poll() / retry branch we added —
/// only the "TX_READ never throws" contract. The poll()/EAGAIN retry loop
/// is exercised on real Linux/vcan hardware CI.
/// </summary>
public class SocketCanBcmOwnershipTests
{
    private static bool ShouldRun()
    {
        var env = Environment.GetEnvironmentVariable("CANKIT_TEST_ADAPTERS");
        return string.Equals(env, "CanKit.Adapter.SocketCAN", StringComparison.OrdinalIgnoreCase);
    }

    // Tracks outstanding rentals so ownership handoffs are directly observable.
    // Only rentals routed through this instance count.
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

    private static ICanBus OpenClassic(string endpoint, IBufferAllocator busAllocator)
        => CanBus.Open(endpoint, cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20)
            .Baud(500_000)
            .BufferAllocator(busAllocator)
            .SetAsyncBufferCapacity(64));

    [Fact]
    public void TransmitPeriodic_Does_Not_Keep_Reference_To_Caller_Buffer()
    {
        if (!ShouldRun()) return;

        // The bus uses its own (default) allocator for BCM's private copy and for
        // any RX rentals; we only track the caller's allocator, so the assertion
        // measures pure caller-side ownership without RX noise polluting it.
        var callerAllocator = new CountingBufferAllocator();
        using var bus = OpenClassic("socketcan://vcan0", new DefaultBufferAllocator());

        IPeriodicTx? handle = null;
        try
        {
            using (var callerOwner = callerAllocator.Rent(3))
            {
                new byte[] { 0xAA, 0xBB, 0xCC }.AsSpan().CopyTo(callerOwner.Memory.Span);

                // ownMemory:false — the caller retains ownership and the frame's
                // Dispose is a no-op; we're relying on the block's `using` for the
                // owner to be the sole disposer.
                using var callerFrame = CanFrame.Classic(0x321, callerOwner, ownMemory: false);
                handle = bus.TransmitPeriodic(callerFrame, new PeriodicTxOptions(TimeSpan.FromMilliseconds(10), -1));

                // Pre-fix behavior: `_frame = frame` would leave BCM referencing
                // this same allocator's rental via `_frame._memoryOwner`. Post-fix,
                // BCMPeriodicTx.Duplicate(bus allocator) copies into a DIFFERENT
                // allocator, so callerAllocator.Outstanding stays at 1.
                callerAllocator.Outstanding.Should().Be(1,
                    "TransmitPeriodic must not rent from the caller's allocator (FR-RAW-005).");
            } // callerOwner disposed here

            // Sole rental is gone; BCM's private copy lives in the bus allocator.
            callerAllocator.Outstanding.Should().Be(0,
                "the caller must be able to dispose its owner without BCM leaking a reference.");
        }
        finally
        {
            handle?.Stop();
        }

        callerAllocator.Outstanding.Should().Be(0);
    }

    [Fact]
    public void RemainingCount_Does_Not_Throw()
    {
        if (!ShouldRun()) return;

        using var bus = OpenClassic("socketcan://vcan0", new DefaultBufferAllocator());
        using var frame = CanFrame.Classic(0x123, new byte[] { 1, 2, 3 });

        using var handle = bus.TransmitPeriodic(frame, new PeriodicTxOptions(TimeSpan.FromMilliseconds(10), -1));

        // Pre-fix: this would throw with EAGAIN from ThrowErrno("read(BCM TX_STATUS)")
        // whenever the kernel hadn't yet enqueued a TX_STATUS reply on the non-blocking
        // query socket. Post-fix: the read races the reply, on EAGAIN we poll+retry
        // up to MaxAttempts, and on unrecoverable failure we fall back to the cached
        // count instead of raising. Under Fake this exercises the happy path only.
        Action act = () => _ = handle.RemainingCount;
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Update_And_Stop_Are_Safe_After_Caller_Disposes_Their_Frame()
    {
        if (!ShouldRun()) return;

        var callerAllocator = new CountingBufferAllocator();
        using var bus = OpenClassic("socketcan://vcan0", new DefaultBufferAllocator());

        IPeriodicTx? handle = null;
        try
        {
            using (var owner = callerAllocator.Rent(4))
            {
                new byte[] { 0x11, 0x22, 0x33, 0x44 }.AsSpan().CopyTo(owner.Memory.Span);
                using var caller = CanFrame.Classic(0x201, owner, ownMemory: false);
                handle = bus.TransmitPeriodic(caller, new PeriodicTxOptions(TimeSpan.FromMilliseconds(5), 3));
            } // caller frame + owner disposed here

            callerAllocator.Outstanding.Should().Be(0,
                "BCM must not hold a lease on the caller's rented buffer.");

            // A subsequent Update() would previously reach through `_frame` back into
            // the caller's now-disposed IMemoryOwner (Can20 path reads _frame.Data.Span
            // in ToCanFrame()). With the fix, `_frame` refers to the duplicated owner
            // rented at TransmitPeriodic time, so Update() is safe even though the
            // caller's owner is gone.
            Action updateWithoutFrame = () => handle!.Update(period: TimeSpan.FromMilliseconds(20));
            updateWithoutFrame.Should().NotThrow();

            Action queryRemaining = () => _ = handle!.RemainingCount;
            queryRemaining.Should().NotThrow();

            await Task.Delay(40);
        }
        finally
        {
            handle?.Stop();
        }

        callerAllocator.Outstanding.Should().Be(0);
    }

    [Fact]
    public void Update_With_New_Frame_Releases_Previous_Bcm_Copy()
    {
        if (!ShouldRun()) return;

        // BCM's private copies live in the bus allocator. To measure just the
        // BCM lifecycle (and not RX rentals), we open a bus with a receive-side
        // filter that drops everything, so no frames land in the RX pipe. The
        // Fake libc still routes BCM emissions but filter-rejected deliveries
        // are dropped without renting.
        var busAllocator = new CountingBufferAllocator();
        using var bus = CanBus.Open("socketcan://vcan0", cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20)
            .Baud(500_000)
            .BufferAllocator(busAllocator)
            // Filter that never matches any of the frames used below (0x201, 0x202).
            .AccMask(0x7FF, 0x7FF, CanFilterIDType.Standard)
            .SetAsyncBufferCapacity(64));

        using var first = CanFrame.Classic(0x201, new byte[] { 1, 2, 3 });
        using var second = CanFrame.Classic(0x202, new byte[] { 9, 9, 9, 9, 9, 9 });

        IPeriodicTx? handle = null;
        try
        {
            handle = bus.TransmitPeriodic(first, new PeriodicTxOptions(TimeSpan.FromMilliseconds(20), -1));
            var afterCtor = busAllocator.Outstanding;

            handle.Update(frame: second);
            var afterUpdate = busAllocator.Outstanding;

            afterCtor.Should().BeGreaterOrEqualTo(1,
                "the ctor's Duplicate must have rented one buffer from the bus allocator.");
            afterUpdate.Should().Be(afterCtor,
                "Update must dispose the previous BCM copy before installing the new one; no net leak.");
        }
        finally
        {
            handle?.Stop();
        }

        busAllocator.Outstanding.Should().Be(0,
            "Stop must dispose the current BCM copy so nothing outlives the periodic handle.");
    }
}
