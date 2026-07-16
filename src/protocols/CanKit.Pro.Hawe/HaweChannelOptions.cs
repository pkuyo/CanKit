using System.Threading;
using CanKit.Pro.Actor;

namespace CanKit.Pro.Hawe
{
    /// <summary>
    /// Optional tuning for a <see cref="HaweChannel"/>. Every field has a safe default so a codec
    /// caller normally does not have to fill this in.
    /// </summary>
    public sealed class HaweChannelOptions
    {
        /// <summary>
        /// Bounded buffer capacity for the channel's demultiplexer subscription (drop-oldest when
        /// full, exactly like every other subscription -- see
        /// <c>CanKit.Pro.RawCan.CanBusService.DefaultBufferCapacity</c>). Null uses the service
        /// default.
        /// </summary>
        public int? SubscriptionBufferCapacity { get; set; }

        /// <summary>
        /// Execution model for the actor loop that drives this channel's codec callbacks. Default
        /// is <see cref="ActorExecutionMode.DedicatedThread"/>, matching every other protocol
        /// instance in CanKit.
        /// </summary>
        /// <remarks>
        /// When set to <see cref="ActorExecutionMode.SynchronizationContext"/>, the channel
        /// constructs its <see cref="ProtocolActor"/> with
        /// <see cref="SynchronizationContext"/> (or <see cref="System.Threading.SynchronizationContext.Current"/>
        /// when that property is null). Both must resolve to a non-null context; otherwise
        /// construction fails with <see cref="System.ArgumentNullException"/>. Construction,
        /// <c>SetSessionState</c>, and detach are safe to invoke from that same context thread:
        /// the channel runs the work inline instead of sync-waiting on a <c>Send</c> marshal.
        /// </remarks>
        public ActorExecutionMode ActorMode { get; set; } = ActorExecutionMode.DedicatedThread;

        /// <summary>
        /// Optional <see cref="System.Threading.SynchronizationContext"/> used when
        /// <see cref="ActorMode"/> is <see cref="ActorExecutionMode.SynchronizationContext"/>.
        /// When null in that mode, <see cref="System.Threading.SynchronizationContext.Current"/>
        /// is used instead. Ignored for every other <see cref="ActorMode"/>.
        /// </summary>
        public SynchronizationContext? SynchronizationContext { get; set; }
    }
}
