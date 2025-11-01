using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Core.Definitions;

public readonly record struct DefaultCanErrorInfo : ICanErrorInfo
{
    public DefaultCanErrorInfo(FrameErrorType Type,
        CanControllerStatus ControllerStatus,
        CanProtocolViolationType ProtocolViolation,
        FrameErrorLocation ProtocolViolationLocation,
        CanTransceiverStatus TransceiverStatus,
        DateTime SystemTimestamp,
        uint RawErrorCode,
        TimeSpan? DeviceTimeSpan,
        FrameDirection Direction,
        byte? ArbitrationLostBit,
        CanErrorCounters? ErrorCounters,
        CanFrame? Frame)
    {
        this.Type = Type;
        this.ControllerStatus = ControllerStatus;
        this.ProtocolViolation = ProtocolViolation;
        this.ProtocolViolationLocation = ProtocolViolationLocation;
        this.TransceiverStatus = TransceiverStatus;
        this.SystemTimestamp = SystemTimestamp;
        this.RawErrorCode = RawErrorCode;
        this.DeviceTimeSpan = DeviceTimeSpan;
        this.Direction = Direction;
        this.ArbitrationLostBit = ArbitrationLostBit;
        this.ErrorCounters = ErrorCounters;
        this.Frame = Frame;
    }

    public FrameErrorType Type { get; init; }
    public CanControllerStatus ControllerStatus { get; init; }
    public CanProtocolViolationType ProtocolViolation { get; init; }
    public FrameErrorLocation ProtocolViolationLocation { get; init; }
    public CanTransceiverStatus TransceiverStatus { get; init; }
    public DateTime SystemTimestamp { get; init; }
    public uint RawErrorCode { get; init; }
    public TimeSpan? DeviceTimeSpan { get; init; }
    public FrameDirection Direction { get; init; }
    public byte? ArbitrationLostBit { get; init; }
    public CanErrorCounters? ErrorCounters { get; init; }
    public CanFrame? Frame { get; init; }

    public void Deconstruct(out FrameErrorType Type, out CanControllerStatus ControllerStatus, out CanProtocolViolationType ProtocolViolation, out FrameErrorLocation ProtocolViolationLocation, out CanTransceiverStatus TransceiverStatus, out DateTime SystemTimestamp, out uint RawErrorCode, out TimeSpan? DeviceTimeSpan, out FrameDirection Direction, out byte? ArbitrationLostBit, out CanErrorCounters? ErrorCounters, out CanFrame? Frame)
    {
        Type = this.Type;
        ControllerStatus = this.ControllerStatus;
        ProtocolViolation = this.ProtocolViolation;
        ProtocolViolationLocation = this.ProtocolViolationLocation;
        TransceiverStatus = this.TransceiverStatus;
        SystemTimestamp = this.SystemTimestamp;
        RawErrorCode = this.RawErrorCode;
        DeviceTimeSpan = this.DeviceTimeSpan;
        Direction = this.Direction;
        ArbitrationLostBit = this.ArbitrationLostBit;
        ErrorCounters = this.ErrorCounters;
        Frame = this.Frame;
    }
}
