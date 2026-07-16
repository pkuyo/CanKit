using System;

namespace CanKit.Pro.Hawe
{
    /// <summary>
    /// A single, running attachment of one <see cref="IHaweCodec"/> onto one
    /// <see cref="CanKit.Pro.RawCan.ICanBusService"/> (SRS FR-HAWE-002/003): the framework side
    /// of the running "HAWE stack instance". Owns the demultiplexer subscription, the actor loop,
    /// the deadline scheduler, and the generic session-skeleton state -- all shared out to the
    /// codec via <see cref="IHaweCodecHost"/>.
    /// </summary>
    /// <remarks>
    /// Disposing the channel is the exclusive way to shut a codec down: it stops delivering
    /// frames, invokes <see cref="IHaweCodec.OnDetached"/> once on the actor loop, and releases
    /// the subscription, the actor and the deadline scheduler. Dispose is idempotent.
    /// </remarks>
    public interface IHaweChannel : IDisposable
    {
        /// <summary>
        /// The codec plugged into this channel. Exposed so callers can look up
        /// <see cref="IHaweCodec.Name"/>/<see cref="IHaweCodec.FramePattern"/> for logging or
        /// diagnostics; the framework itself does not require this handle after construction.
        /// </summary>
        IHaweCodec Codec { get; }

        /// <summary>
        /// The current generic session-skeleton state (SRS FR-HAWE-004). Reads are lock-free but
        /// may race with a concurrent codec-initiated transition; callers that need a coherent
        /// before/after view should observe via
        /// <see cref="IHaweCodec.OnSessionStateChanged"/>.
        /// </summary>
        HaweSessionState SessionState { get; }
    }
}
