using System;

namespace CanKit.Pro.RawCan
{
    /// <summary>
    /// Why a <see cref="TxConfirmation"/> did not confirm the send (only meaningful when
    /// <see cref="TxConfirmation.Confirmed"/> is false). (未确认发送的原因，仅当
    /// <see cref="TxConfirmation.Confirmed"/> 为 false 时有意义。)
    /// </summary>
    public enum TxConfirmFailureReason
    {
        /// <summary>Not a failure: <see cref="TxConfirmation.Confirmed"/> is true. (非失败：确认成功。)</summary>
        None = 0,

        /// <summary>No echo arrived within the configured timeout (FR-RAW-033). (在配置的超时内未收到回显帧。)</summary>
        Timeout,

        /// <summary>
        /// The bus transitioned to <see cref="CanKit.Abstractions.API.Common.Definitions.BusState.BusOff"/>
        /// while the confirmation was outstanding (FR-RAW-033). (等待确认期间总线进入 BusOff 状态。)
        /// </summary>
        BusOff,

        /// <summary>
        /// The driver did not accept the frame at all (<c>ICanBus.Transmit</c>/<c>TransmitAsync</c>
        /// returned 0) (FR-RAW-033). (驱动未接受该帧，<c>Transmit</c>/<c>TransmitAsync</c> 返回 0。)
        /// </summary>
        Rejected,
    }

    /// <summary>
    /// Result of <see cref="ICanBusService.SendConfirmed"/>: a uniform "was this frame actually
    /// sent" answer regardless of whether the underlying adapter supports hardware TX echo
    /// (arc42 §5.3/§6.3, ADR-7; FR-RAW-030..034). (统一的“该帧是否已被发送”结果，无论底层适配器
    /// 是否支持硬件发送回显。)
    /// </summary>
    /// <remarks>
    /// A <see cref="TxConfirmation"/> value is only ever produced for a *resolved* outcome — the
    /// returned <c>Task&lt;TxConfirmation&gt;</c> never completes successfully while the send is
    /// still pending. <see cref="Confirmed"/> is true only for an actual driver-accepted send
    /// (approximated) or an actually-matched echo frame (real); for every other outcome
    /// (timeout, bus-off, outright rejection) <see cref="Confirmed"/> is false and
    /// <see cref="FailureReason"/> explains why (FR-RAW-033). Explicit caller-supplied
    /// <see cref="System.Threading.CancellationToken"/> cancellation is reported as task
    /// cancellation (standard .NET convention), not as a <see cref="TxConfirmation"/> value.
    /// </remarks>
    public readonly record struct TxConfirmation
    {
        /// <summary>
        /// True if the send was confirmed — either by a matched hardware echo, or (when the
        /// adapter has no echo capability enabled) by the driver accepting the frame. See
        /// <see cref="IsApproximated"/> to tell the two apart. (发送是否已确认：或为匹配到的硬件回显，
        /// 或（当适配器未启用回显能力时）为驱动已接受该帧；参见 <see cref="IsApproximated"/> 以区分两者。)
        /// </summary>
        public bool Confirmed { get; init; }

        /// <summary>
        /// True when <see cref="Confirmed"/> reflects driver acceptance rather than an actual
        /// hardware echo (FR-RAW-032) — i.e. "best-effort acknowledgment", not a guarantee the
        /// frame reached the wire. Always false when <see cref="Confirmed"/> is false.
        /// (当 <see cref="Confirmed"/> 反映的是驱动接受而非真实硬件回显时为 true——即“尽力而为的确认”，
        /// 并不保证帧已实际发送到总线上；<see cref="Confirmed"/> 为 false 时恒为 false。)
        /// </summary>
        public bool IsApproximated { get; init; }

        /// <summary>
        /// UTC timestamp of when this result was produced (confirmation or failure).
        /// (产生该结果——无论是确认还是失败——的 UTC 时间戳。)
        /// </summary>
        public DateTime Timestamp { get; init; }

        /// <summary>
        /// Why the send was not confirmed; <see cref="TxConfirmFailureReason.None"/> when
        /// <see cref="Confirmed"/> is true. (发送未被确认的原因；<see cref="Confirmed"/> 为 true 时为
        /// <see cref="TxConfirmFailureReason.None"/>。)
        /// </summary>
        public TxConfirmFailureReason FailureReason { get; init; }
    }
}
