using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Core;
using CanKit.Core.Definitions;
using CanKit.Pro.RawCan;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Verifies the L2 multi-protocol demultiplexing / subscription layer
/// (CanKit.Pro.RawCan, arc42 §5.3 / ADR-5, SRS FR-RAW-010..013) against the Virtual adapter.
/// </summary>
public class RawCanSubscriptionTests : IClassFixture<TestCaseProvider>
{
    private static string NewSession() => $"rawcan-{Guid.NewGuid():N}";

    // Opens a Virtual bus on a unique session so tests never collide.
    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    // Drains up to `count` frames from a subscription, giving up after `timeout`. Delivery on the
    // Virtual hub is synchronous inside Transmit, so a short timeout only guards against a hang if
    // something is broken; the happy path returns as soon as `count` frames are read.
    private static async Task<List<CanFrameView>> Drain(ISubscription sub, int count, TimeSpan timeout)
    {
        var result = new List<CanFrameView>();
        if (count <= 0) return result;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var frame in sub.Frames.WithCancellation(cts.Token))
            {
                result.Add(frame);
                if (result.Count >= count) break;
            }
        }
        catch (OperationCanceledException)
        {
            // timed out waiting for more frames -> return what we have
        }
        return result;
    }

    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(2);

    // FR-RAW-010: two subscriptions with disjoint ID filters on the same bus each receive only
    // their own matching frames.
    [Fact]
    public async Task Disjoint_Id_Filters_Each_Receive_Only_Their_Own_Frames()
    {
        var session = NewSession();
        using var sender = Open(session, 0);
        using var receiver = Open(session, 1);
        using var service = new CanBusService(receiver);

        using var low = service.Subscribe(CanIdFilter.Range(0x100, 0x1FF, CanFilterIDType.Standard));
        using var high = service.Subscribe(CanIdFilter.Range(0x200, 0x2FF, CanFilterIDType.Standard));

        sender.Transmit(CanFrame.Classic(0x100, new byte[] { 1 }));
        sender.Transmit(CanFrame.Classic(0x101, new byte[] { 2 }));
        sender.Transmit(CanFrame.Classic(0x200, new byte[] { 3 }));
        sender.Transmit(CanFrame.Classic(0x201, new byte[] { 4 }));

        var lowFrames = await Drain(low, 2, ShortTimeout);
        var highFrames = await Drain(high, 2, ShortTimeout);

        lowFrames.Select(f => f.ID).Should().Equal(0x100, 0x101);
        highFrames.Select(f => f.ID).Should().Equal(0x200, 0x201);

        // Neither subscription saw the other's traffic: a further short drain yields nothing.
        (await Drain(low, 1, TimeSpan.FromMilliseconds(200))).Should().BeEmpty();
        (await Drain(high, 1, TimeSpan.FromMilliseconds(200))).Should().BeEmpty();
    }

    // FR-RAW-011: a never-drained (worst-case "blocked") subscription must not delay delivery to a
    // second, actively-draining subscription, nor to the bus's own FrameObserved event.
    [Fact]
    public async Task Slow_Subscription_Does_Not_Block_Others_Or_The_Bus_Event()
    {
        var session = NewSession();
        using var sender = Open(session, 0);
        using var receiver = Open(session, 1);
        using var service = new CanBusService(receiver);

        // A direct FrameObserved counter proves the underlying bus event is not stalled either.
        var busEventCount = 0;
        receiver.FrameObserved += (_, _) => Interlocked.Increment(ref busEventCount);

        // Never drained, tiny buffer: its channel fills and drops oldest, but must never block.
        using var slow = service.Subscribe(bufferCapacity: 1);
        // Actively drained, buffer large enough to hold the whole burst.
        using var fast = service.Subscribe(bufferCapacity: 512);

        const int n = 200;
        for (var i = 0; i < n; i++)
            sender.Transmit(CanFrame.Classic(0x300 + (i & 0x0F), new byte[] { (byte)i }));

        var fastFrames = await Drain(fast, n, ShortTimeout);

        fastFrames.Should().HaveCount(n);
        Volatile.Read(ref busEventCount).Should().Be(n);
    }

    // FR-RAW-012: creating and disposing N subscriptions leaves no entries in the service registry.
    [Fact]
    public void Disposing_Subscriptions_Leaves_No_Registry_Entries()
    {
        var session = NewSession();
        using var receiver = Open(session, 0);
        using var service = new CanBusService(receiver);

        service.SubscriptionCount.Should().Be(0);

        var subs = new List<ISubscription>();
        for (var i = 0; i < 20; i++)
            subs.Add(service.Subscribe(CanIdFilter.Range((uint)i, (uint)i, CanFilterIDType.Standard)));

        service.SubscriptionCount.Should().Be(20);

        foreach (var sub in subs)
            sub.Dispose();

        service.SubscriptionCount.Should().Be(0);
    }

    // FR-RAW-014: reconfiguring a subscription's filter at runtime applies to subsequent frames only.
    [Fact]
    public async Task Reconfigure_Filter_At_Runtime_Subsequent_Frames_Follow_New_Criterion()
    {
        var session = NewSession();
        using var sender = Open(session, 0);
        using var receiver = Open(session, 1);
        using var service = new CanBusService(receiver);

        using var sub = service.Subscribe(CanIdFilter.Range(0x100, 0x1FF, CanFilterIDType.Standard));

        sender.Transmit(CanFrame.Classic(0x100, new byte[] { 1 }));
        sender.Transmit(CanFrame.Classic(0x200, new byte[] { 2 }));

        var before = await Drain(sub, 1, ShortTimeout);
        before.Select(f => f.ID).Should().Equal(0x100);

        sub.Reconfigure(CanIdFilter.Range(0x200, 0x2FF, CanFilterIDType.Standard));

        sender.Transmit(CanFrame.Classic(0x101, new byte[] { 3 }));
        sender.Transmit(CanFrame.Classic(0x201, new byte[] { 4 }));

        var after = await Drain(sub, 1, ShortTimeout);
        after.Select(f => f.ID).Should().Equal(0x201);
    }

    // FR-RAW-013: the ID-range/mask fast path matches and excludes correctly (unit-level, no bus).
    [Fact]
    public void IdFilter_FastPath_Matches_And_Excludes()
    {
        static CanFrameView View(int id, bool extended) => new(
            CanFrameType.Can20, id, ReadOnlyMemory<byte>.Empty,
            extended ? FrameFlags.Ext : FrameFlags.None);

        var range = CanIdFilter.Range(0x100, 0x1FF, CanFilterIDType.Standard);
        range.Matches(View(0x100, extended: false)).Should().BeTrue();
        range.Matches(View(0x1FF, extended: false)).Should().BeTrue();
        range.Matches(View(0x0FF, extended: false)).Should().BeFalse();
        range.Matches(View(0x200, extended: false)).Should().BeFalse();
        // Same numeric ID in the other ID space never matches.
        range.Matches(View(0x150, extended: true)).Should().BeFalse();

        var extRange = CanIdFilter.Range(0x100, 0x1FF, CanFilterIDType.Extend);
        extRange.Matches(View(0x150, extended: true)).Should().BeTrue();
        extRange.Matches(View(0x150, extended: false)).Should().BeFalse();

        var mask = CanIdFilter.Mask(0x100, 0x700, CanFilterIDType.Standard);
        mask.Matches(View(0x100, extended: false)).Should().BeTrue();
        mask.Matches(View(0x1FF, extended: false)).Should().BeTrue(); // 0x1FF & 0x700 == 0x100
        mask.Matches(View(0x200, extended: false)).Should().BeFalse(); // 0x200 & 0x700 == 0x200
    }

    // Range(from,to) rejects an inverted range up front.
    [Fact]
    public void IdFilter_Range_Rejects_Inverted_Bounds()
    {
        Action act = () => CanIdFilter.Range(0x200, 0x100);
        act.Should().Throw<ArgumentException>();
    }

    // FR-RAW-012: disposing the same subscription twice is a safe no-op.
    [Fact]
    public void Double_Dispose_Of_Subscription_Is_Safe()
    {
        var session = NewSession();
        using var receiver = Open(session, 0);
        using var service = new CanBusService(receiver);

        var sub = service.Subscribe();
        sub.Dispose();
        sub.Dispose(); // must not throw

        service.SubscriptionCount.Should().Be(0);
    }

    // Disposing the service unwinds subscriptions, detaches from FrameObserved, and delivers no
    // further frames to any of its subscriptions.
    [Fact]
    public async Task Disposing_Service_Stops_Delivery_And_Detaches()
    {
        var session = NewSession();
        using var sender = Open(session, 0);
        using var receiver = Open(session, 1);
        var service = new CanBusService(receiver);
        var sub = service.Subscribe();

        sender.Transmit(CanFrame.Classic(0x123, new byte[] { 1 }));
        (await Drain(sub, 1, ShortTimeout)).Select(f => f.ID).Should().Equal(0x123);

        service.Dispose();
        service.SubscriptionCount.Should().Be(0);

        // After disposal the subscription's stream is completed and no new frames arrive; a drain
        // returns immediately with nothing even though the bus keeps carrying traffic.
        sender.Transmit(CanFrame.Classic(0x124, new byte[] { 2 }));
        (await Drain(sub, 1, TimeSpan.FromMilliseconds(500))).Should().BeEmpty();

        // Double-dispose of the service itself is also safe.
        service.Dispose();
    }

    // Allocator whose owner zeroes its buffer on Dispose, simulating a pooled buffer being
    // returned to the pool and reused by someone else. Used to make "did the subscription queue
    // an aliased view instead of an independent copy" observable deterministically, rather than
    // relying on ArrayPool's actual (unspecified) reuse timing.
    private sealed class PoisoningBufferAllocator : IBufferAllocator
    {
        public IMemoryOwner<byte> Rent(int length, bool zeroFill = false) => new PoisoningOwner(length);

        public bool FrameNeedDispose => true;

        private sealed class PoisoningOwner(int length) : IMemoryOwner<byte>
        {
            private readonly byte[] _buffer = new byte[length];

            public Memory<byte> Memory => _buffer;

            public void Dispose() => Array.Clear(_buffer, 0, _buffer.Length);
        }
    }

    // Regression test for a Bugbot finding on this PR: ICanBus.FrameObserved fires (and TryDeliver
    // enqueues into the subscription's channel) *before* the same frame is handed to the bus's own
    // L1 AsyncFramePipe, which may later dispose it (pool return / reuse) independently of whether
    // this subscription has read its buffered copy yet. Queuing the raw CanFrameView (which aliases
    // that memory) instead of a copy would let the later dispose corrupt an unread buffered frame.
    [Fact]
    public async Task Buffered_Frame_Survives_Its_Source_RX_Lease_Being_Disposed_Afterward()
    {
        var session = NewSession();
        var allocator = new PoisoningBufferAllocator();

        using var sender = Open(session, 0);
        using var receiver = CanBus.Open($"virtual://{session}/1", cfg =>
            cfg.SetProtocolMode(CanProtocolMode.Can20)
                .Baud(TestCaseProvider.AbitRate)
                .BufferAllocator(allocator)
                .SetAsyncBufferCapacity(1)); // tiny L1 pipe: the next frame drops+disposes this one

        using var service = new CanBusService(receiver);
        using var sub = service.Subscribe();

        sender.Transmit(CanFrame.Classic(0x100, new byte[] { 1, 2, 3 }));
        // Overflows the 1-capacity L1 pipe: AsyncFramePipe's DropOldest policy disposes the first
        // frame's CanReceiveData (and, via PoisoningBufferAllocator, zeroes its buffer) right here,
        // synchronously, well before this test ever drains the subscription.
        sender.Transmit(CanFrame.Classic(0x101, new byte[] { 4, 5, 6 }));

        var frames = await Drain(sub, 2, ShortTimeout);

        frames.Select(f => f.Data.ToArray()).Should().BeEquivalentTo(
            new[] { new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 } },
            opts => opts.WithStrictOrdering());
    }

    // Regression test for a second Bugbot finding: a caller-supplied predicate that throws must
    // not suppress delivery to other subscriptions, nor abort the dispatch loop early.
    [Fact]
    public async Task Throwing_Predicate_In_One_Subscription_Does_Not_Suppress_Delivery_To_Others()
    {
        var session = NewSession();
        using var sender = Open(session, 0);
        using var receiver = Open(session, 1);
        using var service = new CanBusService(receiver);

        // Registered first, so it is dispatched before `healthy` in the snapshot order.
        using var broken = service.Subscribe(_ => throw new InvalidOperationException("boom"));
        using var healthy = service.Subscribe();

        sender.Transmit(CanFrame.Classic(0x100, new byte[] { 9 }));

        var healthyFrames = await Drain(healthy, 1, ShortTimeout);
        healthyFrames.Select(f => f.ID).Should().Equal(0x100);
    }
}
