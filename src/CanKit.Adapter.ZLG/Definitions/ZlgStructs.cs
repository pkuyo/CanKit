using System;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Definitions;


/// <summary>
/// Default implementation of CAN error info for ZLG (ZLG 的 CAN 错误信息默认实现)。
/// </summary>
public record ZlgErrorInfo : ICanErrorInfo
{
    /// <summary>
    /// Initialize with raw error code (使用原始错误码初始化)。
    /// </summary>
    public ZlgErrorInfo(uint rawErrorCode)
    {
        RawErrorCode = rawErrorCode;
    }

    /// <summary>
    /// Convert raw code to ZLG error flags (将原始错误码转为 ZLG 错误标志)。
    /// </summary>
    public ZlgErrorFlag ErrorCode => (ZlgErrorFlag)RawErrorCode;

    /// <inheritdoc />
    public FrameErrorType Type { get; init; }

    /// <inheritdoc />
    public CanControllerStatus ControllerStatus { get; init; }

    /// <inheritdoc />
    public CanProtocolViolationType ProtocolViolation { get; init; }

    /// <inheritdoc />
    public FrameErrorLocation ProtocolViolationLocation { get; init; }

    /// <inheritdoc />
    public DateTime SystemTimestamp { get; init; }

    /// <inheritdoc />
    public uint RawErrorCode { get; init; }

    /// <inheritdoc />
    public TimeSpan? DeviceTimeSpan { get; init; }

    /// <inheritdoc />
    public FrameDirection Direction { get; init; }

    /// <inheritdoc />

    public CanErrorCounters? ErrorCounters { get; init; }

    /// <inheritdoc />
    public CanTransceiverStatus TransceiverStatus { get; init; }

    /// <inheritdoc />
    public byte? ArbitrationLostBit { get; init; }

    /// <inheritdoc />
    public ICanFrame? Frame { get; init; }
}
