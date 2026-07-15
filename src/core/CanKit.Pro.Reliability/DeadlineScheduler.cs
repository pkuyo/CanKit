using System;
using System.Threading;
using CanKit.Pro.Actor;

namespace CanKit.Pro.Reliability
{
    /// <summary>
    /// Default <see cref="IDeadlineScheduler"/> (SRS FR-RAW-050): hands out <see cref="Deadline"/>
    /// instances that arm their expiry through a single owning <see cref="IProtocolActor"/>'s
    /// event-driven timer queue. (默认的 <see cref="IDeadlineScheduler"/>（SRS FR-RAW-050）。)
    /// </summary>
    /// <remarks>
    /// Stateless apart from the actor reference: a scheduler is just a factory, and every deadline
    /// it creates is independent. All timing state lives in the <see cref="Deadline"/> and, at the
    /// bottom, in the actor's own single-threaded timer list -- so there is no shared mutable state
    /// in the scheduler itself to synchronize.
    /// </remarks>
    public sealed class DeadlineScheduler : IDeadlineScheduler
    {
        private readonly IProtocolActor _actor;

        /// <summary>
        /// Creates a scheduler whose deadlines fire on <paramref name="actor"/>'s loop.
        /// (创建一个其超时在 <paramref name="actor"/> 循环上触发的调度器。)
        /// </summary>
        /// <param name="actor">
        /// The protocol instance's actor. Reused deliberately (rather than spinning up a private
        /// timer thread) so a fired deadline runs single-writer-safe alongside the rest of that
        /// instance's state (FR-RAW-020/050).
        /// </param>
        public DeadlineScheduler(IProtocolActor actor)
        {
            _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        }

        /// <inheritdoc />
        public IDeadline Arm(TimeSpan timeout, Action onExpired) => new Deadline(_actor, timeout, onExpired);
    }

    /// <summary>
    /// The concrete <see cref="IDeadline"/> created by <see cref="DeadlineScheduler.Arm"/>
    /// (SRS FR-RAW-050). Holds the state machine and the currently scheduled actor-timer handle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The state machine is a single int (<c>_state</c>) driven by <see cref="Interlocked.CompareExchange(ref int,int,int)"/>:
    /// <c>Pending → {Expired | Completed | Cancelled}</c>. Whichever of the expiry callback,
    /// <see cref="Complete"/>, or <see cref="Dispose"/> lands its CAS out of <c>Pending</c> first
    /// wins; the losers become no-ops. A CAS (not a lock) is used for the resolution because the
    /// three racers can come from different threads (the actor loop for expiry; arbitrary caller
    /// threads for Complete/Dispose) and only need a single atomic winner, not mutual exclusion
    /// over a critical section.
    /// </para>
    /// <para>
    /// A separate <c>_lock</c> guards the pairing of "current scheduled handle" with "current
    /// generation" during <see cref="Rearm"/>, because re-arming must atomically (from the point of
    /// view of other Rearm/Complete/Dispose callers) dispose the old actor-timer handle and install
    /// a new one. The generation counter exists because <see cref="IProtocolActor.Schedule"/>'s
    /// handle disposal is only <i>best-effort</i> (a callback already dispatched onto the loop may
    /// still run, per its documented contract): capturing the generation in the scheduled closure
    /// and bailing out when it no longer matches prevents a stale pre-Rearm timer from firing the
    /// post-Rearm deadline. This is honest best-effort, not linearizable -- a Rearm that races an
    /// <i>already-in-flight</i> fire (one that has already passed its generation check on the loop)
    /// can still let that fire win, exactly mirroring the actor's own documented Schedule caveat.
    /// </para>
    /// </remarks>
    internal sealed class Deadline : IDeadline
    {
        private const int StatePending = 0;
        private const int StateExpired = 1;
        private const int StateCompleted = 2;
        private const int StateCancelled = 3;

        private readonly IProtocolActor _actor;
        private readonly Action _onExpired;

        // Guards the (_scheduledHandle, _generation) pair against concurrent Rearm calls, so a
        // re-arm's "dispose old handle, bump generation, arm new handle" sequence is seen atomically
        // by any other Rearm/Complete/Dispose. Not used for the _state resolution itself -- that is
        // a lock-free CAS (see class remarks).
        private readonly object _lock = new();

        // The Pending → terminal resolution. Read via Volatile/Interlocked; written only via CAS.
        private int _state;

        // Bumped on every (re-)arm. The fire callback captures the value current at arm-time and
        // no-ops if it no longer matches, so a stale pre-Rearm timer the actor already dispatched
        // cannot fire the freshly re-armed deadline. Written under _lock via Interlocked.Increment
        // (full barrier + visibility to the lock-free Volatile.Read in Fire).
        private int _generation;

        // The actor-timer handle for the currently armed timeout. Guarded by _lock.
        private IDisposable? _scheduledHandle;

        internal Deadline(IProtocolActor actor, TimeSpan timeout, Action onExpired)
        {
            _actor = actor ?? throw new ArgumentNullException(nameof(actor));
            _onExpired = onExpired ?? throw new ArgumentNullException(nameof(onExpired));
            if (timeout < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must not be negative.");

            // Arm under the lock even though no other thread can observe `this` yet: it keeps the
            // (_scheduledHandle, _generation) invariant in exactly one place (ArmLocked) rather than
            // duplicating the barrier reasoning across construction and Rearm. If the actor is
            // already disposed, Schedule throws ObjectDisposedException straight out of the ctor --
            // an armed deadline requires a live actor.
            lock (_lock)
            {
                ArmLocked(timeout);
            }
        }

        /// <inheritdoc />
        public bool IsExpired => Volatile.Read(ref _state) == StateExpired;

        /// <inheritdoc />
        public bool IsCompleted => Volatile.Read(ref _state) == StateCompleted;

        /// <inheritdoc />
        public bool IsCancelled => Volatile.Read(ref _state) == StateCancelled;

        /// <inheritdoc />
        public bool Rearm(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must not be negative.");

            lock (_lock)
            {
                // Re-arming is only meaningful while Pending: once expired/completed/cancelled the
                // deadline has already resolved and must stay resolved (callers rely on the terminal
                // outcome being final). Checking _state here rather than CAS'ing it -- Rearm never
                // changes the state, it only reschedules the still-pending expiry.
                if (Volatile.Read(ref _state) != StatePending)
                    return false;

                // Best-effort cancel the outstanding timer, then bump the generation so a copy the
                // actor already dispatched onto its loop will no-op (see class remarks). Arm the new
                // one last. ArmLocked may throw ObjectDisposedException if the actor was disposed in
                // the meantime -- we deliberately let that propagate rather than swallow it: a
                // disposed actor means this deadline can no longer be serviced, and the caller must
                // learn that instead of believing the re-arm succeeded.
                _scheduledHandle?.Dispose();
                Interlocked.Increment(ref _generation);
                ArmLocked(timeout);

                // Complete/Dispose resolve _state via a lock-free CAS rather than this lock, so
                // either can win between the check above and ArmLocked finishing. Re-check under
                // the same lock so a concurrent Complete/Dispose isn't reported as a successful
                // re-arm; best-effort cancel the handle we just installed in that case.
                if (Volatile.Read(ref _state) != StatePending)
                {
                    _scheduledHandle?.Dispose();
                    return false;
                }

                return true;
            }
        }

        /// <inheritdoc />
        public bool Complete()
        {
            // CAS is the single point of truth for "who won": if we move Pending → Completed we are
            // the winner and the pending expiry (if any) will lose its own CAS or be gen-guarded
            // out. If we lose, someone else (expiry or a prior Complete/Dispose) already resolved
            // it -- report that we did not win.
            if (Interlocked.CompareExchange(ref _state, StateCompleted, StatePending) != StatePending)
                return false;

            DisposeScheduledHandle();
            return true;
        }

        /// <summary>
        /// Cancels a still-<c>Pending</c> deadline (<see cref="IDeadline"/>'s <c>Cancel</c>).
        /// Idempotent: a second call, or a call after the deadline already expired/completed, is a
        /// harmless no-op. (取消仍处于 Pending 的超时；可重复调用且幂等。)
        /// </summary>
        public void Dispose()
        {
            // Same lock-free resolution as Complete: CAS Pending → Cancelled, then best-effort
            // cancel the timer. Losing the CAS is exactly the idempotent/no-op case.
            if (Interlocked.CompareExchange(ref _state, StateCancelled, StatePending) != StatePending)
                return;

            DisposeScheduledHandle();
        }

        // Precondition: caller holds _lock. Captures the generation current at arm-time into the
        // scheduled closure so a later Rearm can invalidate this exact callback by bumping it.
        private void ArmLocked(TimeSpan timeout)
        {
            var capturedGeneration = _generation;
            _scheduledHandle = _actor.Schedule(timeout, () => Fire(capturedGeneration));
        }

        // Runs on the actor's loop thread (via Schedule), so it is single-writer-safe against every
        // other loop callback -- the whole reason deadlines are composed on the actor instead of a
        // private timer (FR-RAW-050 / Review §1.1 Punkt 10).
        private void Fire(int generation)
        {
            // Generation guard first: a stale pre-Rearm timer that was already dispatched onto the
            // loop before Rearm disposed its handle must not fire the re-armed deadline. Reading
            // _generation without the lock is correct here -- we are on the single loop thread and
            // only need the monotonic "is this still current" answer; a concurrent Rearm bumping it
            // simply makes this stale copy bail.
            if (Volatile.Read(ref _generation) != generation)
                return;

            // Then the terminal CAS: only actually expire if nobody has completed/cancelled us
            // first. If we lose, Complete/Dispose already won and this expiry is a no-op.
            if (Interlocked.CompareExchange(ref _state, StateExpired, StatePending) != StatePending)
                return;

            // We own the transition. Invoke onExpired directly: we are already on the loop, and any
            // exception it throws propagates out of Schedule's callback into the actor's RunSafely,
            // surfacing via the actor's existing BackgroundExceptionOccurred (FR-RAW-023). Adding a
            // try/catch or a second exception channel here would defeat the point of composing on
            // the actor.
            _onExpired();
        }

        private void DisposeScheduledHandle()
        {
            // Under _lock so a concurrent Rearm can't swap _scheduledHandle out from under us (and
            // so we never dispose a handle a Rearm just replaced). TimerHandle.Dispose only flips a
            // cancelled flag and never throws, so this is safe even if the actor is already gone.
            lock (_lock)
            {
                _scheduledHandle?.Dispose();
            }
        }
    }
}
