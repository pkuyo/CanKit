using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Core.Definitions;
using CanKit.Pro.Actor;
using CanKit.Pro.Hawe;
using CanKit.Pro.RawCan;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Verifies the generic public HAWE extension framework (CanKit.Pro.Hawe,
/// SRS FR-HAWE-001..005) against the Virtual adapter. Uses a deliberately generic
/// <see cref="FakePatternCodec"/> that carries no HAWE-specific behaviour -- the framework
/// itself must never require, or expose, proprietary HAWE protocol details (SRS CON-006 / A-6).
/// </summary>
public class HaweFrameworkTests : IClassFixture<TestCaseProvider>
{
    private static string NewSession() => $"hawe-{Guid.NewGuid():N}";

    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(2);

    // A deliberately generic codec used only in the test project: it selects one CAN ID range,
    // counts callbacks, echoes back a fixed byte, and lets the test drive the session skeleton
    // through IHaweCodecHost.SetSessionState. It contains no HAWE-specific frame layout, service
    // id, or state machine -- the framework itself must not require any of those (SRS CON-006).
    private sealed class FakePatternCodec : IHaweCodec
    {
        private readonly TaskCompletionSource<bool> _attachedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FakePatternCodec(HaweFramePattern pattern) => FramePattern = pattern;

        public string Name => "fake-pattern-codec";
        public HaweFramePattern FramePattern { get; }
        public IHaweCodecHost? Host { get; private set; }

        public ConcurrentQueue<CanFrameView> Received { get; } = new();
        public int DetachedCount => Volatile.Read(ref _detached);
        public List<(HaweSessionState prev, HaweSessionState curr)> Transitions { get; } = new();
        public Task Attached => _attachedTcs.Task;

        private int _detached;

        public void OnAttached(IHaweCodecHost host)
        {
            Host = host;
            _attachedTcs.TrySetResult(true);
        }

        public void OnFrameReceived(in CanFrameView frame) => Received.Enqueue(frame);

        public void OnSessionStateChanged(HaweSessionState previous, HaweSessionState current)
            => Transitions.Add((previous, current));

        public void OnDetached() => Interlocked.Increment(ref _detached);
    }

    // Convenience helper: waits (bounded) until the codec has captured `count` frames.
    private static async Task WaitForFrames(FakePatternCodec codec, int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (codec.Received.Count < count && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    // FR-HAWE-001 verification criterion: a codec registered via the public SPI is discoverable
    // by name and the registry hands back a fresh instance per Create call.
    [Fact]
    public void Registry_Registers_And_Creates_Codec_By_Name()
    {
        var registry = new HaweCodecRegistry();
        var pattern = HaweFramePattern.Range(0x700, 0x7FF);

        registry.IsRegistered("fake-pattern-codec").Should().BeFalse();
        registry.Register("fake-pattern-codec", () => new FakePatternCodec(pattern));

        registry.IsRegistered("fake-pattern-codec").Should().BeTrue();
        registry.RegisteredNames.Should().Contain("fake-pattern-codec");

        var codec1 = registry.Create("fake-pattern-codec");
        var codec2 = registry.Create("fake-pattern-codec");
        codec1.Should().NotBeNull();
        codec2.Should().NotBeNull();
        codec1.Should().NotBeSameAs(codec2);
        codec1.Name.Should().Be("fake-pattern-codec");
    }

    [Fact]
    public void Registry_Throws_KeyNotFound_For_Unknown_Codec()
    {
        var registry = new HaweCodecRegistry();
        Action act = () => registry.Create("does-not-exist");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Registry_Unregister_Removes_Factory()
    {
        var registry = new HaweCodecRegistry();
        registry.Register("x", () => new FakePatternCodec(HaweFramePattern.Range(0x100, 0x1FF)));
        registry.Unregister("x").Should().BeTrue();
        registry.Unregister("x").Should().BeFalse();
        registry.IsRegistered("x").Should().BeFalse();
    }

    [Fact]
    public void Registry_Rejects_Null_Or_Empty_Name_On_Register()
    {
        var registry = new HaweCodecRegistry();
        Action nullName = () => registry.Register(null!, () => new FakePatternCodec(HaweFramePattern.Range(0, 0)));
        Action emptyName = () => registry.Register(string.Empty, () => new FakePatternCodec(HaweFramePattern.Range(0, 0)));
        nullName.Should().Throw<ArgumentException>();
        emptyName.Should().Throw<ArgumentException>();
    }

    // FR-HAWE-002 verification criterion: a generic frame pattern is sent/received end-to-end
    // over the Virtual adapter without the framework knowing anything about payload semantics.
    [Fact]
    public async Task Frame_Pattern_Delivers_Matching_Frames_And_Ignores_Others()
    {
        var session = NewSession();
        using var sender = Open(session, 0);
        using var receiver = Open(session, 1);
        using var service = new CanBusService(receiver);

        var codec = new FakePatternCodec(HaweFramePattern.Range(0x300, 0x3FF));
        using var channel = new HaweChannel(service, codec);

        codec.Attached.Status.Should().Be(TaskStatus.RanToCompletion);
        channel.Codec.Should().BeSameAs(codec);
        channel.SessionState.Should().Be(HaweSessionState.Idle);

        sender.Transmit(CanFrame.Classic(0x300, new byte[] { 0xA }));
        sender.Transmit(CanFrame.Classic(0x3FF, new byte[] { 0xB }));
        // Deliberately outside the pattern; must be ignored by this codec.
        sender.Transmit(CanFrame.Classic(0x400, new byte[] { 0xC }));

        await WaitForFrames(codec, 2, ShortTimeout);

        codec.Received.Count.Should().Be(2);
        codec.Received.Select(f => f.ID).Should().BeEquivalentTo(new[] { 0x300, 0x3FF });
    }

    // FR-HAWE-002 / FR-HAWE-003 verification: a codec can send frames back on the same bus via
    // IHaweCodecHost.SendConfirmedAsync (built on ICanBusService's TX-confirm), i.e. the codec
    // reaches the shared L2 services on the same terms as ISO-TP/J1939-TP.
    [Fact]
    public async Task Codec_Can_Send_Frames_Through_Host_On_Same_Bus()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var service = new CanBusService(busA);
        using var mirror = new CanBusService(busB);

        // A vanilla subscription on the *other* bus receives whatever the codec sends via the
        // host: it lets the test observe the transmit end-to-end without inspecting internal
        // service state.
        using var observer = mirror.Subscribe(CanIdFilter.Range(0x555, 0x555));

        var codec = new FakePatternCodec(HaweFramePattern.Range(0x100, 0x1FF));
        using var channel = new HaweChannel(service, codec);
        codec.Attached.Status.Should().Be(TaskStatus.RanToCompletion);

        var frame = CanFrame.Classic(0x555, new byte[] { 1, 2, 3, 4 });
        var confirmation = await codec.Host!.SendConfirmedAsync(frame);
        confirmation.Confirmed.Should().BeTrue();

        using var cts = new CancellationTokenSource(ShortTimeout);
        var received = new List<CanFrameView>();
        try
        {
            await foreach (var f in observer.Frames.WithCancellation(cts.Token))
            {
                received.Add(f);
                break;
            }
        }
        catch (OperationCanceledException) { }

        received.Should().HaveCount(1);
        received[0].ID.Should().Be(0x555);
        received[0].Data.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4 });
    }

    // FR-HAWE-004 verification: the session skeleton exists as a placeholder and can be driven
    // by the codec through IHaweCodecHost.SetSessionState; the framework surfaces every
    // transition to OnSessionStateChanged in order.
    [Fact]
    public async Task Session_Skeleton_Transitions_Are_Observed_By_Codec()
    {
        var session = NewSession();
        using var receiver = Open(session, 0);
        using var service = new CanBusService(receiver);

        var codec = new FakePatternCodec(HaweFramePattern.Range(0x600, 0x6FF));
        using var channel = new HaweChannel(service, codec);
        codec.Attached.Status.Should().Be(TaskStatus.RanToCompletion);

        channel.SessionState.Should().Be(HaweSessionState.Idle);

        codec.Host!.SetSessionState(HaweSessionState.Active).Should().BeTrue();
        codec.Host!.SetSessionState(HaweSessionState.Active).Should().BeFalse(); // no-op
        codec.Host!.SetSessionState(HaweSessionState.Fault).Should().BeTrue();
        codec.Host!.SetSessionState(HaweSessionState.Idle).Should().BeTrue();

        // Give the actor loop a beat to drain any posted callbacks (they are synchronous under
        // PostAsync().GetAwaiter().GetResult(), but the visibility of `Transitions` mutations to
        // this thread is what we wait for here).
        await Task.Delay(50);

        codec.Transitions.Should().Equal(new[]
        {
            (HaweSessionState.Idle, HaweSessionState.Active),
            (HaweSessionState.Active, HaweSessionState.Fault),
            (HaweSessionState.Fault, HaweSessionState.Idle),
        });
        channel.SessionState.Should().Be(HaweSessionState.Idle);
    }

    // FR-HAWE-003 verification: the actor-driven deadline surface is reachable from a codec via
    // the host and fires on the actor loop.
    [Fact]
    public async Task Codec_Can_Arm_Deadline_Through_Host()
    {
        var session = NewSession();
        using var receiver = Open(session, 0);
        using var service = new CanBusService(receiver);

        var codec = new FakePatternCodec(HaweFramePattern.Range(0x200, 0x2FF));
        using var channel = new HaweChannel(service, codec);
        codec.Attached.Status.Should().Be(TaskStatus.RanToCompletion);

        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handle = codec.Host!.ArmDeadline(TimeSpan.FromMilliseconds(50), () => fired.TrySetResult(true));

        // Bounded wait: if the actor-driven deadline never fires within ShortTimeout something
        // is wrong with the framework's wiring to Reliability's DeadlineScheduler.
        var completed = await Task.WhenAny(fired.Task, Task.Delay(ShortTimeout));
        completed.Should().BeSameAs(fired.Task);
    }

    // Disposing the channel must invoke OnDetached exactly once and stop delivering frames to
    // the codec afterwards, even if the underlying bus keeps carrying traffic.
    [Fact]
    public async Task Dispose_Detaches_Codec_And_Stops_Delivery()
    {
        var session = NewSession();
        using var sender = Open(session, 0);
        using var receiver = Open(session, 1);
        using var service = new CanBusService(receiver);

        var codec = new FakePatternCodec(HaweFramePattern.Range(0x100, 0x1FF));
        var channel = new HaweChannel(service, codec);
        codec.Attached.Status.Should().Be(TaskStatus.RanToCompletion);

        sender.Transmit(CanFrame.Classic(0x100, new byte[] { 1 }));
        await WaitForFrames(codec, 1, ShortTimeout);
        codec.Received.Count.Should().Be(1);

        channel.Dispose();
        codec.DetachedCount.Should().Be(1);

        // Traffic on the shared bus continues; the codec must not see any of it now.
        sender.Transmit(CanFrame.Classic(0x101, new byte[] { 2 }));
        await Task.Delay(200);
        codec.Received.Count.Should().Be(1);

        // Double-dispose is safe and must not increment OnDetached a second time.
        channel.Dispose();
        codec.DetachedCount.Should().Be(1);
    }

    // The framework attaches purely on the shared bus service and does not take ownership of it:
    // two independent codecs on disjoint patterns coexist on one service and each see only their
    // own traffic (mirrors the multi-protocol scenario FR-HAWE-003 is aligned with).
    [Fact]
    public async Task Two_Codecs_Share_One_Bus_Service_Without_Cross_Talk()
    {
        var session = NewSession();
        using var sender = Open(session, 0);
        using var receiver = Open(session, 1);
        using var service = new CanBusService(receiver);

        var codecA = new FakePatternCodec(HaweFramePattern.Range(0x100, 0x1FF));
        var codecB = new FakePatternCodec(HaweFramePattern.Range(0x200, 0x2FF));

        using var channelA = new HaweChannel(service, codecA);
        using var channelB = new HaweChannel(service, codecB);

        codecA.Attached.Status.Should().Be(TaskStatus.RanToCompletion);
        codecB.Attached.Status.Should().Be(TaskStatus.RanToCompletion);

        sender.Transmit(CanFrame.Classic(0x100, new byte[] { 1 }));
        sender.Transmit(CanFrame.Classic(0x150, new byte[] { 2 }));
        sender.Transmit(CanFrame.Classic(0x200, new byte[] { 3 }));
        sender.Transmit(CanFrame.Classic(0x2FF, new byte[] { 4 }));

        await WaitForFrames(codecA, 2, ShortTimeout);
        await WaitForFrames(codecB, 2, ShortTimeout);

        codecA.Received.Select(f => f.ID).Should().BeEquivalentTo(new[] { 0x100, 0x150 });
        codecB.Received.Select(f => f.ID).Should().BeEquivalentTo(new[] { 0x200, 0x2FF });
    }

    // FR-HAWE-002: the CanIdFilter-based pattern works with the acceptance-code/mask shape too,
    // not just inclusive ranges.
    [Fact]
    public async Task Frame_Pattern_Accepts_Mask_Filter()
    {
        var session = NewSession();
        using var sender = Open(session, 0);
        using var receiver = Open(session, 1);
        using var service = new CanBusService(receiver);

        // Match all IDs whose lower nibble is 0x1 (0x001, 0x011, 0x021, ...).
        var codec = new FakePatternCodec(HaweFramePattern.Mask(0x001, 0x00F));
        using var channel = new HaweChannel(service, codec);
        codec.Attached.Status.Should().Be(TaskStatus.RanToCompletion);

        sender.Transmit(CanFrame.Classic(0x001, new byte[] { 1 })); // match
        sender.Transmit(CanFrame.Classic(0x011, new byte[] { 2 })); // match
        sender.Transmit(CanFrame.Classic(0x002, new byte[] { 3 })); // no match

        await WaitForFrames(codec, 2, ShortTimeout);
        codec.Received.Select(f => f.ID).Should().BeEquivalentTo(new[] { 0x001, 0x011 });
    }

    // Regression for the SetSessionState reentrancy deadlock: prior to the fix,
    // Host.SetSessionState used PostAsync().GetAwaiter().GetResult() unconditionally, which
    // deadlocked when called from any codec callback already executing on the actor loop --
    // the very loop the pending PostAsync work needs in order to complete. The fix must detect
    // the reentrant call and apply the state transition synchronously.
    [Fact]
    public async Task SetSessionState_From_Codec_Callback_Does_Not_Deadlock()
    {
        var session = NewSession();
        using var sender = Open(session, 0);
        using var receiver = Open(session, 1);
        using var service = new CanBusService(receiver);

        var codec = new ReentrantStateCodec(HaweFramePattern.Range(0x100, 0x1FF));
        using var channel = new HaweChannel(service, codec);
        codec.Attached.Status.Should().Be(TaskStatus.RanToCompletion);

        sender.Transmit(CanFrame.Classic(0x100, new byte[] { 1 }));

        // Bounded wait: pre-fix the callback deadlocks and this never completes; the test times
        // out and fails with a clear "reentrancy deadlock" message.
        var completed = await Task.WhenAny(codec.CallbackDone.Task, Task.Delay(ShortTimeout));
        completed.Should().BeSameAs(codec.CallbackDone.Task,
            "SetSessionState from inside a codec callback must not deadlock the actor loop");

        codec.SetStateReturn.Should().BeTrue("first Idle->Active transition returns true");
        channel.SessionState.Should().Be(HaweSessionState.Active);

        // OnSessionStateChanged must have fired synchronously on the same loop invocation --
        // before the reentrant SetSessionState call returned -- preserving in-order single-writer
        // semantics for state transitions.
        codec.Transitions.Should().ContainSingle()
            .Which.Should().Be((HaweSessionState.Idle, HaweSessionState.Active));
    }

    // Also cover the nested-reentrant case: a codec's OnSessionStateChanged handler itself
    // calls SetSessionState. Because we're still on the actor loop when OnSessionStateChanged
    // fires, this nested call must also take the synchronous path.
    [Fact]
    public async Task Nested_SetSessionState_From_OnSessionStateChanged_Does_Not_Deadlock()
    {
        var session = NewSession();
        using var receiver = Open(session, 0);
        using var service = new CanBusService(receiver);

        var codec = new NestedStateCodec(HaweFramePattern.Range(0x600, 0x6FF));
        using var channel = new HaweChannel(service, codec);
        codec.Attached.Status.Should().Be(TaskStatus.RanToCompletion);

        // The call originates off-loop, hops onto the actor loop, and its OnSessionStateChanged
        // callback then calls SetSessionState(Fault) reentrantly on the same loop invocation.
        var setTask = Task.Run(() => codec.Host!.SetSessionState(HaweSessionState.Active));
        var completed = await Task.WhenAny(setTask, Task.Delay(ShortTimeout));
        completed.Should().BeSameAs(setTask, "nested SetSessionState must not deadlock");
        (await setTask).Should().BeTrue();

        // Both transitions observed, in order.
        await Task.Delay(50);
        codec.Transitions.Should().Equal(new[]
        {
            (HaweSessionState.Idle, HaweSessionState.Active),
            (HaweSessionState.Active, HaweSessionState.Fault),
        });
        channel.SessionState.Should().Be(HaweSessionState.Fault);
    }

    // Regression for the Dispose ordering race: prior to the fix, Dispose posted OnDetached
    // BEFORE awaiting the pump task, so the pump could still Post OnFrameReceived items behind
    // OnDetached in the mailbox and fire them after the codec had been told the channel was
    // detached. The fix must guarantee no OnFrameReceived fires after OnDetached, even under
    // constant traffic during Dispose. We give the race the widest possible window: a slow codec
    // (so the actor mailbox visibly accumulates work behind OnDetached) plus a large burst of
    // frames still resident in the subscription buffer at the moment Dispose runs.
    [Fact]
    public async Task No_OnFrameReceived_Fires_After_OnDetached_During_Dispose()
    {
        var session = NewSession();
        using var sender = Open(session, 0);
        using var receiver = Open(session, 1);
        using var service = new CanBusService(receiver);

        // A modest per-frame delay in the codec keeps enough work in the actor mailbox at any
        // given instant that the OnDetached post lands somewhere in the middle rather than at
        // the very tail -- so any pre-fix late post from the pump is a genuine post-OnDetached
        // fire, not a timing artifact. Small enough that the whole test drains well under the
        // 5 s Dispose timeout.
        var codec = new OrderCapturingCodec(HaweFramePattern.Range(0x100, 0x1FF), perFrameDelayMs: 1);
        var channel = new HaweChannel(service, codec, new HaweChannelOptions { SubscriptionBufferCapacity = 4096 });
        codec.Attached.Status.Should().Be(TaskStatus.RanToCompletion);

        // Continuous background flood: keeps the subscription buffer refilling so the pump is
        // repeatedly inside its "TryRead + Post" hot loop rather than parked at
        // WaitToReadAsync (which would cancel cleanly with no late posts). Bounded by both
        // the flood cancellation and the total frame budget -- we want the race window open
        // during Dispose, not runaway traffic for the whole test.
        using var floodCts = new CancellationTokenSource();
        var flooder = Task.Run(async () =>
        {
            for (var i = 0; i < 200 && !floodCts.IsCancellationRequested; i++)
            {
                try { sender.Transmit(CanFrame.Classic(0x100, new byte[] { (byte)(i & 0xFF) })); }
                catch { break; }
                // Yield often enough that the pump can interleave TryRead/Post iterations with
                // new frames arriving; without this the sender bursts to completion before the
                // pump ever starts and Dispose sees an empty subscription.
                if ((i % 20) == 19) await Task.Delay(1).ConfigureAwait(false);
            }
        });

        // Give the pump time to actually start moving those frames onto the actor loop, so at
        // least a few OnFrameReceived have fired before Dispose runs (proves the pump is
        // active and the subscription buffer is populated).
        var deadline = DateTime.UtcNow + ShortTimeout;
        while (codec.ReceivedCount < 5 && DateTime.UtcNow < deadline)
            await Task.Delay(1);
        codec.ReceivedCount.Should().BeGreaterThan(0, "the pump must be actively delivering frames before Dispose");

        channel.Dispose();
        floodCts.Cancel();
        try { await flooder; } catch { /* transmit may throw once the bus goes away; that is fine */ }

        // The channel is disposed. Give the actor loop one more moment to publish any late
        // post to _framesAfterDetach the pre-fix ordering would have queued behind OnDetached.
        await Task.Delay(50);

        codec.DetachedCount.Should().Be(1);
        codec.FramesAfterDetach.Should().Be(0,
            "OnFrameReceived must never be dispatched after OnDetached on the single-writer loop");
    }

    // Constructor guards.
    [Fact]
    public void HaweChannel_Rejects_Null_Args()
    {
        var codec = new FakePatternCodec(HaweFramePattern.Range(0, 0));
        Action nullService = () => new HaweChannel(null!, codec);
        Action nullCodec = () =>
        {
            var session = NewSession();
            using var bus = Open(session, 0);
            using var service = new CanBusService(bus);
            _ = new HaweChannel(service, null!);
        };
        nullService.Should().Throw<ArgumentNullException>();
        nullCodec.Should().Throw<ArgumentNullException>();
    }

    // ActorExecutionMode.SynchronizationContext is exposed on HaweChannelOptions; the channel must
    // pass a real context through to ProtocolActor (options value, else SynchronizationContext.Current).
    [Fact]
    public void HaweChannel_SynchronizationContext_Mode_Uses_Options_Context()
    {
        var session = NewSession();
        using var bus = Open(session, 0);
        using var service = new CanBusService(bus);
        var codec = new FakePatternCodec(HaweFramePattern.Range(0x100, 0x100));
        var context = new InlineSynchronizationContext();

        using var channel = new HaweChannel(service, codec, new HaweChannelOptions
        {
            ActorMode = ActorExecutionMode.SynchronizationContext,
            SynchronizationContext = context,
        });

        codec.Attached.Status.Should().Be(TaskStatus.RanToCompletion);
        codec.Host.Should().NotBeNull();
        context.SendCount.Should().BeGreaterThan(0,
            "OnAttached must be marshaled through the SynchronizationContext supplied via options");
    }

    // Regression for Bugbot 3596169080: ProtocolActor marshals SyncContext-mode work with blocking
    // Send. Constructing HaweChannel (or calling SetSessionState) from that same dispatcher thread
    // used to PostAsync().GetResult(), which blocks the pump Send needs → permanent deadlock.
    // The channel must detect "caller is on the actor SyncContext" and run the work inline.
    [Fact]
    public void HaweChannel_SyncContext_Mode_From_Dispatcher_Thread_Does_Not_Deadlock()
    {
        using var dispatcher = new QueuingDispatcherSynchronizationContext();
        dispatcher.Start();

        var session = NewSession();
        using var bus = Open(session, 0);
        using var service = new CanBusService(bus);
        var codec = new FakePatternCodec(HaweFramePattern.Range(0x100, 0x100));

        Exception? error = null;
        HaweSessionState? observed = null;
        using var done = new ManualResetEventSlim(false);

        dispatcher.Post(_ =>
        {
            try
            {
                using var channel = new HaweChannel(service, codec, new HaweChannelOptions
                {
                    ActorMode = ActorExecutionMode.SynchronizationContext,
                    SynchronizationContext = dispatcher,
                });

                codec.Host.Should().NotBeNull();
                codec.Host!.SetSessionState(HaweSessionState.Active).Should().BeTrue();
                observed = channel.SessionState;
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        }, null);

        done.Wait(ShortTimeout).Should().BeTrue(
            "constructing HaweChannel / SetSessionState on the SyncContext thread must not deadlock");
        error.Should().BeNull();
        observed.Should().Be(HaweSessionState.Active);
        codec.Attached.Status.Should().Be(TaskStatus.RanToCompletion);
        codec.Transitions.Should().ContainSingle()
            .Which.Should().Be((HaweSessionState.Idle, HaweSessionState.Active));
    }

    // If OnAttached fails after Subscribe + actor start, the demux subscription must not leak.
    [Fact]
    public void HaweChannel_Failed_Construction_Disposes_Partial_Resources()
    {
        var session = NewSession();
        using var bus = Open(session, 0);
        using var service = new CanBusService(bus);
        var codec = new ThrowingOnAttachCodec(HaweFramePattern.Range(0x200, 0x200));

        Action act = () => _ = new HaweChannel(service, codec);
        act.Should().Throw<InvalidOperationException>().WithMessage("attach failed");

        service.SubscriptionCount.Should().Be(0,
            "a failed HaweChannel constructor must dispose the demux subscription it already created");
    }

    private sealed class ThrowingOnAttachCodec : IHaweCodec
    {
        public ThrowingOnAttachCodec(HaweFramePattern pattern) => FramePattern = pattern;
        public string Name => "throwing-on-attach-codec";
        public HaweFramePattern FramePattern { get; }
        public void OnAttached(IHaweCodecHost host) => throw new InvalidOperationException("attach failed");
        public void OnFrameReceived(in CanFrameView frame) { }
        public void OnSessionStateChanged(HaweSessionState previous, HaweSessionState current) { }
        public void OnDetached() { }
    }

    /// <summary>
    /// Runs <see cref="Send"/> inline (like many test / console contexts) and counts invocations
    /// so we can assert marshaling happened.
    /// </summary>
    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        private int _sendCount;
        public int SendCount => Volatile.Read(ref _sendCount);

        public override void Send(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _sendCount);
            d(state);
        }

        public override void Post(SendOrPostCallback d, object? state) => Send(d, state);
    }

    /// <summary>
    /// Minimal UI-style dispatcher: a dedicated thread pumps a queue; <see cref="Send"/> from
    /// other threads blocks until that pump runs the callback; <see cref="Send"/> from the pump
    /// thread itself runs inline. Reproduces the HaweChannel SyncContext deadlock when the
    /// channel sync-waits on PostAsync from the dispatcher thread.
    /// </summary>
    private sealed class QueuingDispatcherSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State, ManualResetEventSlim? Done, Exception?[]? Error)> _queue = new();
        private Thread? _thread;

        public void Start()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        item.Callback(item.State);
                    }
                    catch (Exception ex)
                    {
                        if (item.Error is not null) item.Error[0] = ex;
                        else throw;
                    }
                    finally
                    {
                        item.Done?.Set();
                    }
                }
            })
            {
                IsBackground = true,
                Name = "HaweTest.Dispatcher",
            };
            _thread.Start();
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (ReferenceEquals(Current, this))
            {
                d(state);
                return;
            }

            using var done = new ManualResetEventSlim(false);
            var error = new Exception?[1];
            _queue.Add((d, state, done, error));
            done.Wait();
            if (error[0] is not null)
                throw error[0]!;
        }

        public override void Post(SendOrPostCallback d, object? state)
            => _queue.Add((d, state, null, null));

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread?.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }
    }

    // Calls Host.SetSessionState from inside OnFrameReceived (i.e. from the actor loop).
    // Pre-fix, HaweChannel.Host.SetSessionState always posted+blocked, which deadlocks in this
    // scenario. Post-fix, the reentrant call must apply the transition synchronously.
    private sealed class ReentrantStateCodec : IHaweCodec
    {
        private readonly TaskCompletionSource<bool> _attachedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ReentrantStateCodec(HaweFramePattern pattern) => FramePattern = pattern;
        public string Name => "reentrant-state-codec";
        public HaweFramePattern FramePattern { get; }
        public IHaweCodecHost? Host { get; private set; }
        public Task Attached => _attachedTcs.Task;
        public TaskCompletionSource<bool> CallbackDone { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool SetStateReturn { get; private set; }
        public List<(HaweSessionState prev, HaweSessionState curr)> Transitions { get; } = new();

        public void OnAttached(IHaweCodecHost host)
        {
            Host = host;
            _attachedTcs.TrySetResult(true);
        }

        public void OnFrameReceived(in CanFrameView frame)
        {
            // Reentrant SetSessionState from the actor loop -- pre-fix, this deadlocks.
            SetStateReturn = Host!.SetSessionState(HaweSessionState.Active);
            CallbackDone.TrySetResult(true);
        }

        public void OnSessionStateChanged(HaweSessionState previous, HaweSessionState current)
            => Transitions.Add((previous, current));

        public void OnDetached() { }
    }

    // A codec whose OnSessionStateChanged handler itself calls SetSessionState. Verifies the
    // nested reentrant case is also handled synchronously and does not deadlock.
    private sealed class NestedStateCodec : IHaweCodec
    {
        private readonly TaskCompletionSource<bool> _attachedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _nested;
        public NestedStateCodec(HaweFramePattern pattern) => FramePattern = pattern;
        public string Name => "nested-state-codec";
        public HaweFramePattern FramePattern { get; }
        public IHaweCodecHost? Host { get; private set; }
        public Task Attached => _attachedTcs.Task;
        public List<(HaweSessionState prev, HaweSessionState curr)> Transitions { get; } = new();

        public void OnAttached(IHaweCodecHost host)
        {
            Host = host;
            _attachedTcs.TrySetResult(true);
        }

        public void OnFrameReceived(in CanFrameView frame) { }

        public void OnSessionStateChanged(HaweSessionState previous, HaweSessionState current)
        {
            Transitions.Add((previous, current));
            if (!_nested && current == HaweSessionState.Active)
            {
                _nested = true;
                // Nested reentrant call from within OnSessionStateChanged -- must not deadlock.
                Host!.SetSessionState(HaweSessionState.Fault);
            }
        }

        public void OnDetached() { }
    }

    // Records the order of callbacks: any OnFrameReceived that runs after OnDetached is a
    // regression of the Dispose ordering fix. All bookkeeping happens on the single-writer actor
    // loop, so plain (non-volatile) counters are fine.
    private sealed class OrderCapturingCodec : IHaweCodec
    {
        private readonly TaskCompletionSource<bool> _attachedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _perFrameDelayMs;
        private int _received;
        private int _detached;
        private int _framesAfterDetach;
        public OrderCapturingCodec(HaweFramePattern pattern, int perFrameDelayMs = 0)
        {
            FramePattern = pattern;
            _perFrameDelayMs = perFrameDelayMs;
        }
        public string Name => "order-capturing-codec";
        public HaweFramePattern FramePattern { get; }
        public Task Attached => _attachedTcs.Task;
        public int ReceivedCount => Volatile.Read(ref _received);
        public int DetachedCount => Volatile.Read(ref _detached);
        public int FramesAfterDetach => Volatile.Read(ref _framesAfterDetach);

        public void OnAttached(IHaweCodecHost host) => _attachedTcs.TrySetResult(true);

        public void OnFrameReceived(in CanFrameView frame)
        {
            // Ordering check is on the actor loop, so a plain read of _detached is authoritative:
            // any nonzero value here means OnDetached already ran and this OnFrameReceived is a
            // regression.
            if (_detached != 0) Interlocked.Increment(ref _framesAfterDetach);
            Interlocked.Increment(ref _received);
            // Slow the actor loop's frame consumption so the mailbox visibly backs up behind any
            // work Dispose posts, giving the pre-fix race the widest possible window to lose.
            if (_perFrameDelayMs > 0) Thread.Sleep(_perFrameDelayMs);
        }

        public void OnSessionStateChanged(HaweSessionState previous, HaweSessionState current) { }

        public void OnDetached() => Interlocked.Increment(ref _detached);
    }
}
