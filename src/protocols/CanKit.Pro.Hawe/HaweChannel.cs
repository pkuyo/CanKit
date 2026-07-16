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

        // Non-null only in ActorExecutionMode.SynchronizationContext. Used to detect the classic
        // "sync-over-async on the dispatcher thread" deadlock: ProtocolActor marshals with
        // SynchronizationContext.Send, so a PostAsync().GetAwaiter().GetResult() call made *from*
        // that same context blocks the pump Send needs and hangs forever. See RunOnActorLoop.
        private readonly SynchronizationContext? _actorSyncContext;

        // Guarded via Interlocked; the channel's own state, distinct from the codec-driven session
        // state, so a Dispose racing with a codec callback is fully deterministic.
        private int _disposed;

        // Set by Dispose *before* it starts awaiting the pump: read by PumpAsync and by Post-time
        // helpers to stop enqueuing new codec callbacks once teardown is in progress. Ordering rule
        // is "no OnFrameReceived after OnDetached"; Dispose enforces that by (1) cancelling the
        // pump, (2) awaiting the pump task so no further OnFrameReceived can be posted, and only
        // then (3) posting OnDetached. This flag is the belt-and-braces defense against a frame
        // slipping through between (1) and (2) -- the pump checks it before each Post.
        private int _detachStarted;

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

            // Locals until construction succeeds: if anything below throws after Subscribe / actor
            // start, the catch path disposes those partial resources. Calling Dispose() is not
            // safe here -- _pumpTask may not be assigned yet, and OnAttached may never have run.
            ISubscription? subscription = null;
            ProtocolActor? actor = null;
            try
            {
                subscription = _busService.Subscribe(codec.FramePattern.Filter, opts.SubscriptionBufferCapacity);

                // SynchronizationContext mode needs a non-null context (ProtocolActor ctor). Prefer
                // an explicit options value; otherwise fall back to SynchronizationContext.Current
                // so UI / ASP.NET-style callers can select the mode without plumbing the context.
                var syncContext = opts.ActorMode == ActorExecutionMode.SynchronizationContext
                    ? opts.SynchronizationContext ?? SynchronizationContext.Current
                    : null;
                actor = new ProtocolActor(opts.ActorMode, syncContext);

                _subscription = subscription;
                _actor = actor;
                _actorSyncContext = syncContext;
                _deadlines = new DeadlineScheduler(_actor);
                _host = new Host(this);

                // Attach on the actor loop before starting the pump: the OnAttached callback is
                // guaranteed to see zero frames delivered, matching every other protocol instance's
                // "attach first, then receive" ordering. RunOnActorLoop waits synchronously so the
                // constructor's contract ("codec attached before this returns") is a hard guarantee,
                // not a race -- and avoids PostAsync().GetResult() when called from the actor's
                // own SynchronizationContext (UI/dispatcher), which would deadlock on Send.
                RunOnActorLoop(() => _codec.OnAttached(_host));

                _pumpTask = Task.Run(PumpAsync);
            }
            catch
            {
                try { subscription?.Dispose(); } catch { /* best-effort teardown */ }
                try { actor?.Dispose(); } catch { /* best-effort teardown */ }
                _pumpCts.Dispose();
                throw;
            }
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
                    // Stop enqueuing new frames the instant Dispose signals detach: keeps the
                    // "no OnFrameReceived after OnDetached" ordering intact even against the
                    // narrow window between _pumpCts.Cancel() and the enumerator actually
                    // observing the cancellation on its next MoveNext.
                    if (Volatile.Read(ref _detachStarted) != 0) break;

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

            // Signal detach-in-progress to the pump *before* cancelling: the pump checks this
            // flag between iterations and stops enqueuing further OnFrameReceived posts, so a
            // frame that arrived just before Cancel() cannot slip past into the mailbox behind
            // the OnDetached we post below.
            Volatile.Write(ref _detachStarted, 1);
            _pumpCts.Cancel();

            // Wait for the pump task to end BEFORE posting OnDetached. This is the core of the
            // "no OnFrameReceived after OnDetached" guarantee: once _pumpTask has completed no
            // further _actor.Post(() => OnFrameReceived(...)) can ever be issued, so posting
            // OnDetached here places it strictly after every OnFrameReceived that will ever run
            // on the single-writer actor loop. Doing this in the opposite order (post OnDetached
            // first, then await pump) let the pump enqueue late frames behind OnDetached and
            // fire them after the codec had already been told the channel was detached.
            try { _pumpTask.Wait(TimeSpan.FromSeconds(5)); }
            catch { /* pump exits on cancel; ignore any residual AggregateException */ }

            // Now, and only now, hand OnDetached to the actor loop. The codec is guaranteed
            // exactly-once OnDetached on the same single-writer loop as every other callback,
            // closing out any codec-owned resources safely. If the actor is already torn down
            // for some reason, swallow the exception and continue -- Dispose must not throw for
            // an already-broken channel. Inline when already on the actor SyncContext so we do
            // not PostAsync().Wait on the dispatcher thread (Send deadlock); otherwise wait with
            // a bounded timeout so a stuck actor cannot hang Dispose forever.
            try
            {
                if (_isOnActorLoop.Value || IsOnActorSyncContext())
                    RunOnActorLoop(() => _codec.OnDetached());
                else
                    _actor.PostAsync(WrapOnLoop(() => _codec.OnDetached())).Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // See remark above: teardown is best-effort past this point.
            }

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

        // True when the caller is already executing on the SynchronizationContext that
        // ProtocolActor.Send marshals onto. Reference equality matches how UI dispatchers install
        // a single context instance as Current on their thread.
        private bool IsOnActorSyncContext()
            => _actorSyncContext is not null
               && ReferenceEquals(SynchronizationContext.Current, _actorSyncContext);

        /// <summary>
        /// Runs <paramref name="work"/> under the channel's single-writer discipline and waits for
        /// it to finish. Three paths, in priority order:
        /// <list type="number">
        /// <item>Already inside a WrapOnLoop callback → invoke inline (reentrancy; avoids
        /// self-deadlock on the actor mailbox).</item>
        /// <item>Calling from the actor's SynchronizationContext → invoke inline (avoids the
        /// SyncContext UI deadlock: PostAsync+GetResult blocks the dispatcher that
        /// ProtocolActor.Send needs to deliver the posted work).</item>
        /// <item>Otherwise → PostAsync and block for the result (DedicatedThread / ThreadPool /
        /// SyncContext called from a non-dispatcher thread).</item>
        /// </list>
        /// Inline paths preserve single-writer: while this thread runs the callback, a concurrent
        /// ProtocolActor.Send onto the same context cannot proceed until we return to the pump.
        /// </summary>
        private void RunOnActorLoop(Action work)
        {
            if (_isOnActorLoop.Value)
            {
                work();
                return;
            }

            if (IsOnActorSyncContext())
            {
                WrapOnLoop(work)();
                return;
            }

            _actor.PostAsync(WrapOnLoop(work)).GetAwaiter().GetResult();
        }

        private T RunOnActorLoop<T>(Func<T> work)
        {
            if (_isOnActorLoop.Value)
                return work();

            if (IsOnActorSyncContext())
                return WrapOnLoop(work)();

            return _actor.PostAsync(WrapOnLoop(work)).GetAwaiter().GetResult();
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
                // RunOnActorLoop covers both deadlock classes: (1) reentrant calls from inside a
                // codec callback already on the actor loop, and (2) off-loop calls made from the
                // actor's own SynchronizationContext / UI thread, where PostAsync().GetResult()
                // would block the dispatcher ProtocolActor.Send needs. Nested SetSessionState
                // from inside OnSessionStateChanged takes the reentrant path via WrapOnLoop.
                return _channel.RunOnActorLoop(() => ApplyStateChange(state));
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
