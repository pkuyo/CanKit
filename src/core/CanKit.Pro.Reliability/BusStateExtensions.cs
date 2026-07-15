using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Pro.Reliability
{
    /// <summary>
    /// Small, pure classification helpers over <see cref="BusState"/> (SRS FR-RAW-051), so protocol
    /// instances can express "may I still transmit?" / "is the bus degraded?" without repeating the
    /// same enum comparisons at every call site. (针对 <see cref="BusState"/> 的纯分类辅助方法（SRS FR-RAW-051）。)
    /// </summary>
    public static class BusStateExtensions
    {
        /// <summary>
        /// True only for <see cref="BusState.BusOff"/>: the controller has removed itself from the
        /// bus and cannot transmit at all until it recovers, so a controlled TX must be aborted
        /// (FR-RAW-051). Error-warning/passive states still allow transmission (the controller is
        /// merely degraded), so they are deliberately <i>not</i> treated as transmit-blocking here.
        /// (仅当 <see cref="BusState.BusOff"/> 时为 true：控制器已脱离总线，无法发送。)
        /// </summary>
        public static bool IsTransmitBlocked(this BusState state) => state == BusState.BusOff;

        /// <summary>
        /// True for <see cref="BusState.ErrWarning"/>, <see cref="BusState.ErrPassive"/>, and
        /// <see cref="BusState.BusOff"/>: the bus has left the healthy <see cref="BusState.ErrActive"/>
        /// range and a protocol may want to pause/slow down or surface a warning (FR-RAW-051), even
        /// where transmission is still technically possible. (总线已离开健康状态，协议可能希望暂停或降速。)
        /// </summary>
        public static bool IsDegraded(this BusState state) =>
            state == BusState.ErrWarning || state == BusState.ErrPassive || state == BusState.BusOff;
    }
}
