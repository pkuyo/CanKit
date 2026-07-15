using System;
using CanKit.Pro.Actor;

namespace CanKit.Pro.Reliability
{
    /// <summary>
    /// A reusable, actor-driven timeout primitive (SRS FR-RAW-050). Protocol layers (ISO-TP N_Bs/
    /// N_Cr, J1939 timeouts, UDS P2/P2*, CANopen SDO, ...) arm a <see cref="IDeadline"/> for a
    /// time-bounded state transition and are notified via <c>onExpired</c> when the transition did
    /// not complete in time. (可复用的、由 Actor 驱动的超时原语（SRS FR-RAW-050）。)
    /// </summary>
    /// <remarks>
    /// This directly addresses the deep-code-review finding "Deadlines werden gepflegt, aber nie
    /// geprüft" (Review §1.1 Punkt 10): a deadline scheduled here is composed on top of the owning
    /// <see cref="IProtocolActor"/>'s own event-driven timer queue (<see cref="IProtocolActor.Schedule"/>),
    /// so its expiry is guaranteed to actually fire and be checked on the actor's loop -- it can
    /// never sit as inert data that is written but never re-examined. Because every protocol
    /// instance already runs on a <see cref="IProtocolActor"/> (FR-RAW-020), this is deliberately
    /// not an independent standalone timer with its own thread; reusing the actor's loop is what
    /// keeps the fired callback single-writer-safe against the rest of the instance's state.
    /// </remarks>
    public interface IDeadlineScheduler
    {
        /// <summary>
        /// Arms a new deadline that will invoke <paramref name="onExpired"/> on the owning actor's
        /// loop once <paramref name="timeout"/> has elapsed, unless it is completed or cancelled
        /// first. (装载一个新的超时。)
        /// </summary>
        /// <param name="timeout">
        /// Time until expiry. Must be &gt;= <see cref="TimeSpan.Zero"/>; a zero timeout fires on the
        /// next loop iteration.
        /// </param>
        /// <param name="onExpired">
        /// Invoked exactly once, on the actor's loop, if and only if the deadline expires before it
        /// is completed or cancelled. Any exception it throws propagates out of the actor's
        /// <see cref="IProtocolActor.Schedule"/> callback and is surfaced through the actor's own
        /// <see cref="IProtocolActor.BackgroundExceptionOccurred"/> channel (FR-RAW-023) -- there is
        /// deliberately no second exception channel here.
        /// </param>
        /// <returns>A handle used to complete, re-arm, cancel (<see cref="IDisposable.Dispose"/>), or inspect the deadline.</returns>
        IDeadline Arm(TimeSpan timeout, Action onExpired);
    }
}
