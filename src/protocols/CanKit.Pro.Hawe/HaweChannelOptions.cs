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
        public ActorExecutionMode ActorMode { get; set; } = ActorExecutionMode.DedicatedThread;
    }
}
