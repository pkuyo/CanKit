using System;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Pro.Actor;
using CanKit.Pro.RawCan;
using CanKit.Pro.Reliability;

namespace CanKit.Pro.Hawe
{
    /// <summary>
    /// Default <see cref="IHaweChannel"/> implementation: wires one <see cref="IHaweCodec"/> to
    /// one <see cref="ICanBusService"/> using the same L2 building blocks every other L3/L4 stack
    /// in CanKit uses -- a filtered demultiplexer subscription (SRS FR-RAW-010..013), a
    /// single-mailbox <see cref="ProtocolActor"/> (SRS FR-RAW-020..024) and a
    /// <see cref="DeadlineScheduler"/> (SRS FR-RAW-050). The channel itself is deliberately thin:
    /// all HAWE-specific decisions live behind <see cref="IHaweCodec"/> in a private module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The channel does not construct or take ownership of the underlying
    /// <see cref="ICanBusService"/>: several codecs may share one service (each with its own
    /// disjoint <see cref="HaweFramePattern"/>), and the caller normally owns the service
    /// lifetime already. Disposing the channel therefore only unwinds this channel's own
    /// resources: subscription, actor, deadline scheduler, plus one
    /// <see cref="IHaweCodec.OnDetached"/> call on the actor loop.
    /// </para>
    /// <para>
    /// Every codec callback (<see cref="IHaweCodec.OnAttached"/>,
    /// <see cref="IHaweCodec.OnFrameReceived"/>, <see cref="IHaweCodec.OnSessionStateChanged"/>,
    /// <see cref="IHaweCodec.OnDetached"/>) runs on the actor's single-writer loop. Frames are
    /// pushed onto that loop by the demux subscription (a background task drains the
    /// subscription's async enumerable and posts each frame). This preserves the same
    /// single-writer guarantee ISO-TP relies on so the codec's internal state needs no locking.
    /// </para>
    /// </remarks>
    public sealed class HaweChannel : IHaweChannel
    {
        private readonly IHaweCodec _codec;
        private readonly ICanBusService _busService;
        private readonly ProtocolActor _actor;
        private readonly DeadlineScheduler _deadlines;
        private readonly ISubscription _subscription;
        private readonly CancellationTokenSource _pumpCts = new();
        private readonly Task _pumpTask;
        private readonly Host _host;

        // Guarded via Interlocked; the channel's own state, distinct from the codec-driven session
        // state, so a Dispose racing with a codec callback is fully deterministic.
        private int _disposed;

        // Codec-driven generic session state (FR-HAWE-004). Kept as an int for lock-free reads
        // from any thread via Volatile.Read; writes are serialized on the actor loop by
        // Host.SetSessionState, which is the only mutator.
        private int _sessionState;

        // True for the duration of a codec callback that this channel itself dispatched to the
        // actor loop (OnAttached, OnFrameReceived, OnSessionStateChanged, OnDetached, ArmDeadline
        // fires, Host.Post work). Lets Host.SetSessionState detect reentrant calls from those
        // callbacks and apply the state transition synchronously instead of
        // PostAsync().GetAwaiter().GetResult() -- which would deadlock, because the actor loop
        // that would service that posted work is the very loop currently blocked inside the
        // callback that just called SetSessionState. AsyncLocal (rather than [ThreadStatic]) so
        // the flag correctly follows ExecutionContext through any await boundary a callback might
        // introduce, and is scoped to this channel instance so nested channels don't interfere.
        private readonly AsyncLocal<bool> _isOnActorLoop = new();

        /// <summary>
        /// Attaches <paramref name="codec"/> to <paramref name="busService"/> and starts pumping
        /// matching frames onto the codec's actor loop. Invokes
        /// <see cref="IHaweCodec.OnAttached"/> once, on that loop, before returning control -- so
        /// callers can call <see cref="Host"/>-facing helpers immediately after construction.
        /// </summary>
        /// <param name="busService">The shared L2 demux service to attach onto. Not owned by the channel.</param>
        /// <param name="codec">The private codec implementation. Owned exclusively by this channel.</param>
        /// <param name="options">Optional tuning; null uses defaults.</param>
        public HaweChannel(ICanBusService busService, IHaweCodec codec, HaweChannelOptions? options = null)
        {
            _busService = busService ?? throw new ArgumentNullException(nameof(busService));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            var opts = options ?? new HaweChannelOptions();

            _subscription = _busService.Subscribe(codec.FramePattern.Filter, opts.SubscriptionBufferCapacity);
            _actor = new ProtocolActor(opts.ActorMode);
            _deadlines = new DeadlineScheduler(_actor);
            _host = new Host(this);

            // Attach on the actor loop before starting the pump: the OnAttached callback is
            // guaranteed to see zero frames delivered, matching every other protocol instance's
            // "attach first, then receive" ordering. Wait synchronously so the constructor's
            // contract ("codec attached before this returns") is a hard guarantee, not a race.
            // Safe from the SetSessionState-style deadlock: the constructor is by definition not
            // running on the actor loop yet, so PostAsync().GetAwaiter().GetResult() here cannot
            // be a reentrant self-wait.
            _actor.PostAsync(WrapOnLoop(() => _codec.OnAttached(_host))).GetAwaiter().GetResult();

            _pumpTask = Task.Run(PumpAsync);
        }

        /// <inheritdoc />
        public IHaweCodec Codec => _codec;

        /// <inheritdoc />
        public HaweSessionState SessionState => (HaweSessionState)Volatile.Read(ref _sessionState);

        private async Task PumpAsync()
        {
            try
            {
                await foreach (var frame in _subscription.Frames.WithCancellation(_pumpCts.Token).ConfigureAwait(false))
                {
                    // Capture-by-value into the closure: CanFrameView is a struct, and the reference
                    // captured here is the buffered copy the subscription already owns (see
                    // Subscription.TryDeliver's payload-copy comment), so posting it to the actor
                    // loop is safe even after the subscription's async enumerable has moved on.
                    var f = frame;
                    _actor.Post(WrapOnLoop(() => _codec.OnFrameReceived(in f)));
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on Dispose(): _pumpCts fires and the enumeration ends.
            }
            catch (Exception)
            {
                // The subscription's async enumerable does not surface a fault channel other than
                // completing early; if anything unexpected escapes we treat it as end-of-stream and
                // let Dispose finish the teardown normally. A codec that needs to observe pump
                // failures can surface that via its own OnDetached-time bookkeeping.
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            // Cancel the frame pump first so no new OnFrameReceived posts land after we start
            // tearing the codec down. The subscription itself is disposed below.
            _pumpCts.Cancel();

            // Post OnDetached onto the actor loop and wait for it (best effort): the codec is
            // guaranteed exactly-once OnDetached on the same single-writer loop as every other
            // callback, closing out any codec-owned resources safely. If the actor is already
            // torn down for some reason, swallow the exception and continue -- Dispose must not
            // throw for an already-broken channel.
            try
            {
                _actor.PostAsync(WrapOnLoop(() => _codec.OnDetached())).Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // See remark above: teardown is best-effort past this point.
            }

            try { _pumpTask.Wait(TimeSpan.FromSeconds(5)); }
            catch { /* pump exits on cancel; ignore any residual AggregateException */ }

            _subscription.Dispose();
            _actor.Dispose();
            _pumpCts.Dispose();
        }

        // Every codec callback the channel dispatches to the actor loop goes through this wrapper
        // so _isOnActorLoop is true for the duration of the callback. Host.SetSessionState reads
        // that flag to route reentrant calls onto the synchronous fast path instead of
        // PostAsync().GetAwaiter().GetResult() (see the field's own remark for the full rationale).
        private Action WrapOnLoop(Action work)
        {
            return () =>
            {
                var previous = _isOnActorLoop.Value;
                _isOnActorLoop.Value = true;
                try { work(); }
                finally { _isOnActorLoop.Value = previous; }
            };
        }

        private Func<T> WrapOnLoop<T>(Func<T> work)
        {
            return () =>
            {
                var previous = _isOnActorLoop.Value;
                _isOnActorLoop.Value = true;
                try { return work(); }
                finally { _isOnActorLoop.Value = previous; }
            };
        }

        // The IHaweCodecHost surface. Kept as a private nested class so the framework's own
        // instance state (actor, deadlines, session state) is never exposed to the codec beyond
        // the documented interface.
        private sealed class Host : IHaweCodecHost
        {
            private readonly HaweChannel _channel;

            internal Host(HaweChannel channel) => _channel = channel;

            public ICanBusService BusService => _channel._busService;

            public HaweSessionState SessionState => _channel.SessionState;

            public Task<TxConfirmation> SendConfirmedAsync(CanFrame frame, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
                => _channel._busService.SendConfirmed(frame, timeout, cancellationToken);

            public bool SetSessionState(HaweSessionState state)
            {
                // Reentrant fast path: if this call is happening from inside a codec callback the
                // channel dispatched to the actor loop (OnFrameReceived, ArmDeadline fires,
                // Host.Post work, OnAttached/OnDetached), we are already on the single-writer loop
                // -- the state mutation and OnSessionStateChanged fire can run synchronously,
                // preserving the same "callbacks always on the loop, in order" discipline. Going
                // through PostAsync().GetAwaiter().GetResult() in this case would deadlock: the
                // loop cannot process the newly posted work while it is blocked inside the very
                // callback that just called us.
                if (_channel._isOnActorLoop.Value)
                    return ApplyStateChange(state);

                // Off-loop caller: hop onto the actor loop and wait for the result. Wrapped so a
                // nested SetSessionState from inside OnSessionStateChanged is also detected as
                // reentrant and takes the synchronous path above.
                return _channel._actor.PostAsync(_channel.WrapOnLoop(() => ApplyStateChange(state)))
                    .GetAwaiter().GetResult();
            }

            private bool ApplyStateChange(HaweSessionState state)
            {
                var previous = (HaweSessionState)Volatile.Read(ref _channel._sessionState);
                if (previous == state) return false;
                Volatile.Write(ref _channel._sessionState, (int)state);
                _channel._codec.OnSessionStateChanged(previous, state);
                return true;
            }

            public IDisposable ArmDeadline(TimeSpan timeout, Action onExpired)
                => _channel._deadlines.Arm(timeout, _channel.WrapOnLoop(onExpired));

            public void Post(Action work) => _channel._actor.Post(_channel.WrapOnLoop(work));
        }
    }
}
