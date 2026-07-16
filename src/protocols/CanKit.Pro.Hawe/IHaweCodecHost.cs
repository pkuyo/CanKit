using System;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Pro.RawCan;

namespace CanKit.Pro.Hawe
{
    /// <summary>
    /// The framework-side of the codec/host contract (SRS FR-HAWE-003): the set of L2 services a
    /// private <see cref="IHaweCodec"/> may call from its callbacks in order to send frames,
    /// schedule timers, and drive the generic session-skeleton state. Handed to the codec exactly
    /// once via <see cref="IHaweCodec.OnAttached(IHaweCodecHost)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every method on this host is safe to call only from the channel's actor loop -- i.e. from
    /// inside a codec callback or from a scheduled deadline callback. This is the same
    /// single-writer discipline every ISO-TP / J1939-TP / CANopen instance already relies on, so
    /// the codec does not have to marshal frames onto its own thread or take its own locks.
    /// </para>
    /// <para>
    /// The framework surfaces raw CAN transmit through <see cref="SendConfirmedAsync"/>, backed
    /// by the same <see cref="ICanBusService.SendConfirmed"/> primitive that ISO-TP already uses;
    /// this keeps HAWE on parity with every other L3/L4 stack and makes the confirmation
    /// semantics (hardware-echo vs. driver-acceptance approximation) identical.
    /// </para>
    /// </remarks>
    public interface IHaweCodecHost
    {
        /// <summary>
        /// The underlying <see cref="ICanBusService"/> the channel is demultiplexing. Exposed so
        /// specialised codecs that need a secondary filtered subscription (e.g. a debug tap) can
        /// register one directly, using the same primitive as the primary
        /// <see cref="IHaweCodec.FramePattern"/>. Codecs that only need the primary flow can
        /// ignore this.
        /// </summary>
        ICanBusService BusService { get; }

        /// <summary>
        /// The current generic session-skeleton state
        /// (SRS FR-HAWE-004). See <see cref="HaweSessionState"/>. Reads from the actor loop only.
        /// </summary>
        HaweSessionState SessionState { get; }

        /// <summary>
        /// Sends <paramref name="frame"/> on the shared bus and asynchronously reports whether
        /// it was actually sent, using the same TX-confirm semantics as every other L3/L4 stack
        /// (SRS FR-RAW-030..034). The returned task completes on some pool thread; if the codec
        /// needs to react on the actor loop it must post the result back explicitly. The
        /// framework does not itself dispose <paramref name="frame"/>; the caller keeps ownership
        /// per the CanKit TX-lease contract.
        /// </summary>
        /// <param name="frame">The frame to transmit.</param>
        /// <param name="timeout">
        /// Maximum time to wait for a hardware echo (when the bus has echo enabled). Null uses the
        /// service's default. Ignored on the driver-acceptance approximation path.
        /// </param>
        /// <param name="cancellationToken">Caller-supplied cancellation.</param>
        Task<TxConfirmation> SendConfirmedAsync(CanFrame frame, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Transitions the channel's generic session-skeleton state and, if it actually changes,
        /// invokes <see cref="IHaweCodec.OnSessionStateChanged"/> on the actor loop before this
        /// call returns. The framework attaches no meaning to any specific transition; the codec
        /// is the sole authority on session semantics (SRS FR-HAWE-004).
        /// </summary>
        /// <param name="state">The new state.</param>
        /// <returns>True if the state changed, false if it was already <paramref name="state"/>.</returns>
        bool SetSessionState(HaweSessionState state);

        /// <summary>
        /// Arms a one-shot deadline that fires on the actor loop after
        /// <paramref name="timeout"/> unless the returned handle is completed or disposed first.
        /// Composed on top of the same <c>DeadlineScheduler</c> every other protocol instance
        /// uses (SRS FR-RAW-050), so a codec that models its own timing (P2, N_As, session
        /// keep-alive) gets deadlines that are guaranteed to be checked.
        /// </summary>
        /// <param name="timeout">Time until expiry. Must be &gt;= <see cref="TimeSpan.Zero"/>.</param>
        /// <param name="onExpired">Callback invoked at most once, on the actor loop.</param>
        /// <returns>A handle used to complete or cancel the deadline. Disposing cancels it.</returns>
        IDisposable ArmDeadline(TimeSpan timeout, Action onExpired);

        /// <summary>
        /// Schedules <paramref name="work"/> to run on the actor loop as a fire-and-forget item.
        /// The codec uses this to defer work triggered outside a callback (e.g. from an
        /// application thread) onto the single-writer loop. Exceptions surface via the actor's
        /// documented background-exception channel, exactly like every other post.
        /// </summary>
        /// <param name="work">The work to run on the actor loop.</param>
        void Post(Action work);
    }
}
