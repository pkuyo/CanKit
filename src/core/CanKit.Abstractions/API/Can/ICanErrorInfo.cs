using System;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Abstractions.API.Can;

/// <summary>
/// Additional information describing an error frame. (用于描述错误帧的附加信息。)
/// </summary>
public interface ICanErrorInfo
{
    /// <summary>
    /// Gets the error type. (获取错误类型。)
    /// </summary>
    FrameErrorType Type { get; init; }

    /// <summary>
    /// Controller status. (控制器状态。)
    /// </summary>
    CanControllerStatus ControllerStatus { get; init; }

    /// <summary>
    /// Protocol violation type. (协议违规类型。)
    /// </summary>
    CanProtocolViolationType ProtocolViolation { get; init; }

    /// <summary>
    /// Location where the protocol violation occurred. (协议违规发生位置。)
    /// </summary>
    FrameErrorLocation ProtocolViolationLocation { get; init; }

    /// <summary>
    /// Transceiver status. (收发器状态。)
    /// </summary>
    CanTransceiverStatus TransceiverStatus { get; init; }

    /// <summary>
    /// Gets the system timestamp. (获取系统时间戳。)
    /// </summary>
    DateTime SystemTimestamp { get; init; }

    /// <summary>
    /// Gets the raw error code. (获取原始错误码。)
    /// </summary>
    uint RawErrorCode { get; init; }

    /// <summary>
    /// Gets the device time offset. (获取设备时间偏移量。)
    /// </summary>
    TimeSpan? DeviceTimeSpan { get; init; }

    /// <summary>
    /// Gets the direction of the error frame. (获取错误帧的方向。)
    /// </summary>
    FrameDirection Direction { get; init; }

    /// <summary>
    /// Arbitration lost bit position (0–31). Null if unknown. (仲裁丢失位位置（0–31）。若未知则为 null。)
    /// </summary>
    byte? ArbitrationLostBit { get; init; }

    /// <summary>
    /// Bus error counters. (总线错误计数。)
    /// </summary>
    CanErrorCounters? ErrorCounters { get; init; }

    /// <summary>
    /// Gets the associated raw frame. (获取相关的原始帧。)
    /// </summary>
    CanFrame? Frame { get; init; }
}
