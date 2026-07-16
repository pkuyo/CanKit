using CanKit.Abstractions.API.Can.Definitions;

namespace CanKit.Pro.Hawe
{
    /// <summary>
    /// The public extension point (SPI) through which a private, proprietary HAWE codec module
    /// plugs into the generic framework (SRS FR-HAWE-001). All HAWE-specific knowledge -- payload
    /// layout, service catalogue, session/handshake state machine, cryptographic material, error
    /// vocabulary -- lives behind this interface, in an implementation shipped from a separate,
    /// non-public repository (SRS CON-006 / A-6). The framework itself only ever sees raw CAN
    /// frames and opaque codec callbacks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A codec instance is owned by exactly one <see cref="IHaweChannel"/> for the lifetime of
    /// that channel: <see cref="OnAttached"/> is invoked once, before any frame is delivered, and
    /// <see cref="OnDetached"/> is invoked once, after which no further callbacks fire. Both
    /// <see cref="OnFrameReceived"/> and <see cref="OnSessionStateChanged"/> run on the channel's
    /// single actor loop, so the codec implementation is guaranteed single-writer against its own
    /// internal state and does not need to take its own locks (mirrors the
    /// <c>CanKit.Pro.Actor</c> contract used by every other L3/L4 stack).
    /// </para>
    /// <para>
    /// The framework never inspects a codec's identity beyond <see cref="Name"/> (used for
    /// diagnostics/registry lookup) and <see cref="FramePattern"/> (used to size the
    /// demultiplexer subscription). It ships no reference/default implementation of this
    /// interface: any codec sitting behind it is deliberately out of scope for this open-source
    /// package.
    /// </para>
    /// </remarks>
    public interface IHaweCodec
    {
        /// <summary>
        /// Short, human-readable name used in registry lookups (<see cref="IHaweCodecRegistry"/>)
        /// and diagnostic logs. Must be unique within a single registry instance. Deliberately not
        /// versioned or namespaced -- the framework does not care whether two codecs are compatible
        /// with each other, only that a caller can find the codec it registered.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The generic frame-selection pattern that tells the channel which frames on the shared
        /// bus belong to this codec (SRS FR-HAWE-002). Read once during
        /// <see cref="IHaweChannel"/> construction; must not change afterwards.
        /// </summary>
        HaweFramePattern FramePattern { get; }

        /// <summary>
        /// Called exactly once, on the channel's actor loop, before any other callback. The codec
        /// captures the <paramref name="host"/> handle if it needs to send frames or read session
        /// state later; it must not use the handle from any other thread than the actor loop.
        /// </summary>
        /// <param name="host">
        /// The framework side of the codec/host contract: the codec's transmit / state-change /
        /// deadline surface. Only valid until <see cref="OnDetached"/> is invoked.
        /// </param>
        void OnAttached(IHaweCodecHost host);

        /// <summary>
        /// Invoked on the channel's actor loop for every CAN frame that matches
        /// <see cref="FramePattern"/>. The framework has done no HAWE-specific decoding; the
        /// codec receives the same read-only <see cref="CanFrameView"/> the demultiplexer would
        /// have delivered to any other subscription.
        /// </summary>
        /// <param name="frame">The matching frame. Non-owning; do not retain past this callback.</param>
        void OnFrameReceived(in CanFrameView frame);

        /// <summary>
        /// Invoked on the channel's actor loop whenever the framework's generic session-skeleton
        /// state (<see cref="HaweSessionState"/>) transitions. The framework itself never triggers
        /// a transition; every change originates from a codec call to
        /// <see cref="IHaweCodecHost.SetSessionState(HaweSessionState)"/>. Codecs that do not
        /// use the session skeleton can leave this as a no-op.
        /// </summary>
        /// <param name="previous">The state the channel was in before the transition.</param>
        /// <param name="current">The state the channel is now in.</param>
        void OnSessionStateChanged(HaweSessionState previous, HaweSessionState current);

        /// <summary>
        /// Invoked exactly once, on the channel's actor loop, when the channel is being torn down
        /// (either the caller disposed it, or the underlying bus became irrecoverable). Any
        /// codec-owned resources (buffers, timers, files) must be released here; the codec must
        /// not touch <see cref="IHaweCodecHost"/> after this returns.
        /// </summary>
        void OnDetached();
    }
}
