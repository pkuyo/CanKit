using System;

namespace CanKit.Pro.Reliability
{
    /// <summary>
    /// A single armed deadline (SRS FR-RAW-050). A deadline starts <c>Pending</c> and resolves
    /// exactly once into one of three terminal outcomes: it <b>expires</b> (its <c>onExpired</c>
    /// callback fired), it is <b>completed</b> (the awaited transition finished in time), or it is
    /// <b>cancelled</b> (disposed). (单个已装载的超时（SRS FR-RAW-050）。)
    /// </summary>
    /// <remarks>
    /// The three terminal outcomes are mutually exclusive under normal operation: whichever of the
    /// expiry callback, <see cref="Complete"/>, or <see cref="IDisposable.Dispose"/> reaches the internal state
    /// field first "wins" the transition out of <c>Pending</c>, and the others become no-ops. This
    /// lets a caller (e.g. a UDS client tracking a P2 window) ask "did I complete before the
    /// deadline fired?" via <see cref="Complete"/>'s return value.
    /// </remarks>
    public interface IDeadline : IDisposable
    {
        /// <summary>
        /// True once the deadline's timeout elapsed and its <c>onExpired</c> callback won the race
        /// to fire. (超时已到期并触发回调时为 true。)
        /// </summary>
        bool IsExpired { get; }

        /// <summary>
        /// True once <see cref="Complete"/> won the race, i.e. the awaited transition finished
        /// before the timeout. (在超时前调用 <see cref="Complete"/> 成功后为 true。)
        /// </summary>
        bool IsCompleted { get; }

        /// <summary>
        /// True once the deadline was cancelled via <see cref="IDisposable.Dispose"/> before it
        /// expired or completed. (在到期/完成前经 <see cref="IDisposable.Dispose"/> 取消后为 true。)
        /// </summary>
        bool IsCancelled { get; }

        /// <summary>
        /// Extends (or shortens) a still-<c>Pending</c> deadline to a new timeout measured from now,
        /// e.g. an ISO-TP receiver refreshing N_Cr on each consecutive frame. (将仍处于 Pending 的
        /// 超时重新设定为自当前时刻起的新时长。)
        /// </summary>
        /// <param name="timeout">New time until expiry, measured from now. Must be &gt;= <see cref="TimeSpan.Zero"/>.</param>
        /// <returns>
        /// True if the deadline was still <c>Pending</c> and has been re-armed; false if it had
        /// already expired, completed, or been cancelled (in which case nothing changes).
        /// </returns>
        bool Rearm(TimeSpan timeout);

        /// <summary>
        /// Marks a still-<c>Pending</c> deadline as completed, cancelling its pending expiry.
        /// (将仍处于 Pending 的超时标记为已完成，取消其到期触发。)
        /// </summary>
        /// <returns>
        /// True if this call won the race and moved the deadline from <c>Pending</c> to
        /// <c>Completed</c>; false if the deadline had already expired, completed, or been cancelled
        /// (idempotent no-op). The return value is the caller's answer to "did I finish before the
        /// deadline fired?".
        /// </returns>
        bool Complete();
    }
}
