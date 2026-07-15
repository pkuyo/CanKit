using System;
using System.Threading;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Pro.Actor;

namespace CanKit.Pro.Reliability
{
    /// <summary>
    /// Event args for <see cref="BusStateMonitor.StateChanged"/> (SRS FR-RAW-051): carries both the
    /// previous and the newly-observed <see cref="BusState"/> so a subscriber can react to the
    /// specific transition (e.g. only abort on entering <see cref="BusState.BusOff"/>, only resume
    /// on leaving it). (<see cref="BusStateMonitor.StateChanged"/> 的事件参数（SRS FR-RAW-051）。)
    /// </summary>
    public sealed class BusStateChangedEventArgs : EventArgs
    {
        /// <summary>Creates the args for a transition from <paramref name="previous"/> to <paramref name="current"/>.</summary>
        public BusStateChangedEventArgs(BusState previous, BusState current)
        {
            Previous = previous;
            Current = current;
        }

        /// <summary>The last state observed before this transition. (此次转换前观测到的状态。)</summary>
        public BusState Previous { get; }

        /// <summary>The state observed now, which differs from <see cref="Previous"/>. (当前观测到的状态。)</summary>
        public BusState Current { get; }
    }

    /// <summary>
    /// Pushes <see cref="ICanBus.BusState"/> transitions to a protocol instance so it can abort or
    /// pause controlled transmissions on degradation (ErrWarning/ErrPassive/BusOff) and resume once
    /// the bus recovers (SRS FR-RAW-051). (将 <see cref="ICanBus.BusState"/> 的状态转换推送给协议实例。)
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ICanBus.BusState"/> is a plain getter with no dedicated change event, and an
    /// adapter's <see cref="ICanBus.ErrorFrameReceived"/>/<see cref="ICanBus.FaultOccurred"/> are
    /// not guaranteed to fire on <i>every</i> transition. The reliable mechanism is therefore a
    /// <b>self-rearming poll driven through the owning <see cref="IProtocolActor"/>'s own
    /// <see cref="IProtocolActor.Schedule"/></b> (default 50 ms) rather than a
    /// <see cref="System.Threading.Timer"/> or a free-running thread -- this keeps the monitor
    /// inside the existing single-mailbox event-driven-actor model (FR-RAW-020..022) instead of
    /// reintroducing a busy-loop/free-running-timer anti-pattern. The two bus events are subscribed
    /// <i>additionally</i>, purely as low-latency hints: each triggers an immediate out-of-band
    /// recheck (<see cref="IProtocolActor.Post"/>) so e.g. a <see cref="BusState.BusOff"/> is
    /// observed near-instantly instead of waiting up to one poll interval. The hint deliberately
    /// does not touch the poll timer; the self-rearming poll is the independent reliability floor.
    /// </para>
    /// <para>
    /// <b>Loop-thread cost:</b> each poll tick reads <see cref="ICanBus.BusState"/> synchronously on
    /// the actor's own loop thread. If a particular adapter's <c>BusState</c> getter is slow or
    /// blocking, that stalls this protocol instance's loop for the duration -- a known, accepted
    /// tradeoff of reusing the actor (which keeps state-change handling single-writer-safe) rather
    /// than a bug to work around here.
    /// </para>
    /// <para>
    /// <b>Lifetime:</b> the poll loop also stops on its own once the owning actor is disposed
    /// (a re-arm then observes <see cref="ObjectDisposedException"/> and quietly ceases). Calling
    /// <see cref="Dispose"/> is still required to detach the two bus event subscriptions, which are
    /// independent of the actor's lifetime.
    /// </para>
    /// </remarks>
    public sealed class BusStateMonitor : IDisposable
    {
        // Default poll cadence: fast enough that a BusOff is noticed within ~50 ms even if no
        // ErrorFrame/Fault hint ever fires, cheap enough to be negligible on an otherwise-idle loop.
        private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);

        private readonly ICanBus _bus;
        private readonly IProtocolActor _actor;
        private readonly TimeSpan _pollInterval;

        private readonly EventHandler<ICanErrorInfo> _errorHint;
        private readonly EventHandler<Exception> _faultHint;
        // Whether each hint subscription actually took: some adapters reject ErrorFrameReceived
        // unless configured for it (e.g. AllowErrorInfo=false). A rejected hint is non-fatal -- the
        // poll is the reliability floor -- so we degrade to poll-only and remember not to detach a
        // subscription we never made.
        private readonly bool _errorHintSubscribed;
        private readonly bool _faultHintSubscribed;

        // Last-observed state, as an int for lock-free Volatile access. Written only on the actor
        // loop (poll tick / hint recheck), read from any thread via CurrentState. Because the loop
        // is the single writer, it can trust its own last write without a lock.
        private int _stateRaw;

        private IDisposable? _pollHandle; // the currently scheduled poll tick; best-effort cancelled on Dispose
        private int _disposed;

        /// <summary>
        /// Wraps <paramref name="bus"/> and drives its state polling through <paramref name="actor"/>.
        /// (包装 <paramref name="bus"/>，并通过 <paramref name="actor"/> 驱动状态轮询。)
        /// </summary>
        /// <param name="bus">The bus whose <see cref="ICanBus.BusState"/> is observed.</param>
        /// <param name="actor">The protocol instance's actor; the poll runs on its loop (FR-RAW-020/051).</param>
        /// <param name="pollInterval">
        /// Poll cadence; must be &gt; <see cref="TimeSpan.Zero"/> when given. Defaults to 50 ms.
        /// </param>
        public BusStateMonitor(ICanBus bus, IProtocolActor actor, TimeSpan? pollInterval = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _actor = actor ?? throw new ArgumentNullException(nameof(actor));
            if (pollInterval is { } pi && pi <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(pollInterval), "Poll interval must be positive.");
            _pollInterval = pollInterval ?? DefaultPollInterval;

            // Baseline the current state synchronously, right here in the constructor. This is safe
            // even without synchronization: it runs before any other thread can observe `this`, so
            // there is no writer to race with yet. It also means CurrentState is meaningful the
            // instant the constructor returns, before the first poll tick.
            _stateRaw = (int)_bus.BusState;

            _errorHint = OnBusHint;
            _faultHint = OnFaultHint;

            // Subscribe the hints best-effort: a controlled TX abort must not be prevented just
            // because an adapter won't surface error frames. If a subscription throws (adapter
            // configuration), fall back to poll-only for that channel.
            try
            {
                _bus.ErrorFrameReceived += _errorHint;
                _errorHintSubscribed = true;
            }
            catch
            {
                _errorHintSubscribed = false;
            }

            try
            {
                _bus.FaultOccurred += _faultHint;
                _faultHintSubscribed = true;
            }
            catch
            {
                _faultHintSubscribed = false;
            }

            // Arm the first poll tick on the loop. Posting (rather than scheduling directly) keeps
            // all timer bookkeeping originating from the actor's own thread, consistent with how the
            // rest of the monitor's state is touched.
            _actor.Post(RearmPoll);
        }

        /// <summary>
        /// The most recently observed <see cref="BusState"/>. Reflects the bus's actual state at
        /// construction time and is updated on every observed transition. (最近观测到的 <see cref="BusState"/>。)
        /// </summary>
        public BusState CurrentState => (BusState)Volatile.Read(ref _stateRaw);

        /// <summary>
        /// Raised on the actor's loop whenever the observed state differs from the last-seen one --
        /// for both degrading (e.g. ErrActive → BusOff) and recovering (e.g. BusOff → ErrActive)
        /// transitions, since a protocol needs to know when to resume, not only when to abort
        /// (FR-RAW-051). Edge-triggered: never raised while the state is unchanged. (仅在状态变化时触发。)
        /// </summary>
        public event EventHandler<BusStateChangedEventArgs>? StateChanged;

        /// <inheritdoc />
        public void Dispose()
        {
            // Idempotent: only the first caller runs the teardown. The poll loop itself also stops
            // once it next observes _disposed != 0 (or the actor is gone); Dispose exists chiefly to
            // detach the bus event subscriptions, which outlive the actor otherwise.
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (_errorHintSubscribed)
            {
                try { _bus.ErrorFrameReceived -= _errorHint; } catch { /* bus tearing down; nothing else to do */ }
            }
            if (_faultHintSubscribed)
            {
                try { _bus.FaultOccurred -= _faultHint; } catch { /* bus tearing down; nothing else to do */ }
            }

            // Best-effort cancel the outstanding poll (a tick already dispatched onto the loop may
            // still run once, but it will see _disposed and not re-arm -- so the loop goes quiet
            // within at most one poll interval).
            Volatile.Read(ref _pollHandle)?.Dispose();
        }

        private void OnBusHint(object? sender, ICanErrorInfo e) => PostRecheck();

        private void OnFaultHint(object? sender, Exception e) => PostRecheck();

        // Latency optimization only: an immediate out-of-band recheck on the loop. Deliberately does
        // NOT rearm/reset the poll timer -- the self-rearming poll is independent and remains the
        // reliability floor; this merely shortens the observation latency of a transition.
        private void PostRecheck()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            try
            {
                _actor.Post(RecheckOnLoop);
            }
            catch (ObjectDisposedException)
            {
                // Actor already disposed; the poll loop has (or will) stop on its own. Nothing to do.
            }
        }

        // The scheduled poll tick body. Structured so the next poll is re-armed even if the state
        // read throws: a transient failure reading BusState must not permanently kill monitoring.
        // The original exception still propagates out of this Schedule callback to surface via the
        // actor's BackgroundExceptionOccurred (FR-RAW-023); RearmPoll swallows only its own
        // ObjectDisposedException so a throwing finally can't mask that original exception.
        private void PollTick()
        {
            try
            {
                RecheckOnLoop();
            }
            finally
            {
                RearmPoll();
            }
        }

        private void RecheckOnLoop()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            RaiseIfChanged();
        }

        // Runs on the actor's loop, so it is the single writer of _stateRaw and StateChanged is
        // raised serially with the rest of the instance's work (no re-entrancy, no lock needed).
        private void RaiseIfChanged()
        {
            var previous = (BusState)_stateRaw; // loop is the sole writer -> trust the last write
            var current = _bus.BusState;        // synchronous getter, on this loop thread (see remarks)
            if (current == previous)
                return;

            Volatile.Write(ref _stateRaw, (int)current);
            StateChanged?.Invoke(this, new BusStateChangedEventArgs(previous, current));
        }

        private void RearmPoll()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            try
            {
                Volatile.Write(ref _pollHandle, _actor.Schedule(_pollInterval, PollTick));
            }
            catch (ObjectDisposedException)
            {
                // The owning actor was disposed concurrently. Monitoring simply goes quiet -- we
                // must not rethrow here, both so the poll loop ends cleanly and so, when called from
                // PollTick's finally, this cannot mask an in-flight exception from the poll body.
            }
        }
    }
}
