using System;
using CanKit.Core.Definitions;

namespace CanKit.Core.Abstractions;

/// <summary>
/// Represents a controllable periodic transmit task.
/// 表示一个可控的周期发送任务。
/// </summary>
public interface IPeriodicTx : IDisposable
{
    /// <summary>
    /// Current period between transmits.
    /// 当前发送周期。
    /// </summary>
    TimeSpan Period { get; }

    /// <summary>
    /// sends times, -1 for infinite.
    /// 发送次数，-1 表示无限。
    /// </summary>
    int RepeatCount { get; }

    /// <summary>
    /// remaining sends, -1 for infinite.
    /// 剩余发送次数，-1 表示无限。
    /// </summary>
    int RemainingCount { get; }

    /// <summary>
    /// Stop the periodic task.
    /// 停止周期任务。
    /// </summary>
    void Stop();

    /// <summary>
    /// Update the frame and/or period and/or remaining count.
    /// 运行时更新帧/周期/剩余次数。
    /// </summary>
    /// <param name="frame">New frame to send, null to keep unchanged。</param>
    /// <param name="period">New period, null to keep unchanged。</param>
    /// <param name="repeatCount">New remaining count, null to keep unchanged。</param>
    void Update(ICanFrame? frame = null, TimeSpan? period = null, int? repeatCount = null);

    /// <summary>
    /// Raised when the task finishes due to reaching the repeat count.
    /// 因达到次数而结束时触发。
    /// </summary>
    event EventHandler? Completed;
}
