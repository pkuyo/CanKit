namespace CanKit.Pro.Hawe
{
    /// <summary>
    /// The generic, protocol-agnostic session states surfaced by the framework's placeholder
    /// session skeleton (SRS FR-HAWE-004). The framework deliberately defines no HAWE-specific
    /// transitions, guards, or actions -- those live entirely inside a private codec once the
    /// (confidential) protocol specification becomes available (SRS A-6 / FR-HAWE-005). Until then
    /// this three-state alphabet is a Vorlage / placeholder only: a codec is free to keep the
    /// channel in <see cref="Idle"/> forever, or to model its own richer state machine internally
    /// and drive this enum purely as a public health signal for the caller.
    /// </summary>
    public enum HaweSessionState
    {
        /// <summary>
        /// The channel is attached and configured but no session-level activity is in progress.
        /// The initial state of every freshly-opened channel.
        /// </summary>
        Idle = 0,

        /// <summary>
        /// The codec has declared that a session-level exchange is currently in progress. The
        /// framework attaches no further meaning to this state; it does not, for example, gate
        /// frame delivery on it.
        /// </summary>
        Active = 1,

        /// <summary>
        /// The codec has declared a session-level fault that is not, by itself, a bus fault. The
        /// framework does not automatically recover from this state; a codec transitions back to
        /// <see cref="Idle"/> (or <see cref="Active"/>) when it considers the fault handled.
        /// </summary>
        Fault = 2,
    }
}
