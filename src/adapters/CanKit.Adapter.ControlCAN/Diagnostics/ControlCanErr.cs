using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CcApi = CanKit.Adapter.ControlCAN.Native.ControlCAN;

namespace CanKit.Adapter.ControlCAN.Diagnostics;

internal static class ControlCanErr
{
    public const uint StatusOk = 1;
    public static ICanErrorInfo ToErrorInfo(in CcApi.VCI_ERR_INFO err)
    {
        // ECC fields (same layout as ZLG):
        // passive_ErrData[0]: ECC (type, dir, location)
        // passive_ErrData[1]: REC, passive_ErrData[2]: TEC
        byte ecc = err.Passive_ErrData != null && err.Passive_ErrData.Length >= 1 ? err.Passive_ErrData[0] : (byte)0;
        int rec = err.Passive_ErrData != null && err.Passive_ErrData.Length >= 2 ? err.Passive_ErrData[1] : 0;
        int tec = err.Passive_ErrData != null && err.Passive_ErrData.Length >= 3 ? err.Passive_ErrData[2] : 0;

        byte eccType = (byte)((ecc >> 6) & 0x03);   // 0=Bit,1=Form,2=Stuff,3=Other
        byte eccDirBit = (byte)((ecc >> 5) & 0x01); // 0=Tx, 1=Rx
        byte eccLoc = (byte)(ecc & 0x1F);

        var dir = eccDirBit switch
        {
            0 => FrameDirection.Tx,
            1 => FrameDirection.Rx,
            _ => FrameDirection.Unknown
        };

        // ControlCAN ErrCode uses the same flag semantics as ZLG error flags
        var flags = (ControlCanErrorFlag)err.ErrCode;
        var type = MapFlagsToType(flags);
        var loc = MapEccLocToLocation(eccLoc);
        var ctrl = MapFlagsToController(flags) | CanKit.Core.Utils.CanKitExtension.ToControllerStatus(rec, tec);
        var prot = MapEccTypeToProt(eccType);

        if (eccType == 3)
        {
            if (loc == FrameErrorLocation.AckSlot || loc == FrameErrorLocation.AckDelimiter)
                type |= FrameErrorType.AckError;
            else
                type |= FrameErrorType.ProtocolViolation;
        }
        else if (prot != CanProtocolViolationType.None)
        {
            type |= FrameErrorType.ProtocolViolation;
        }

        byte? arbBit = ((flags & ControlCanErrorFlag.ArbitrationLost) != 0 && err.ArLost_ErrData < 32)
            ? err.ArLost_ErrData
            : null;

        return new DefaultCanErrorInfo(
            type,
            ctrl,
            prot,
            loc,
            CanTransceiverStatus.Unspecified,
            DateTime.Now,
            err.ErrCode,
            null,
            dir,
            arbBit,
            new CanErrorCounters
            {
                TransmitErrorCounter = tec,
                ReceiveErrorCounter = rec
            },
            null);
    }

    private static FrameErrorLocation MapEccLocToLocation(byte loc) => loc switch
    {
        0x00 => FrameErrorLocation.Unspecified,
        0x03 => FrameErrorLocation.StartOfFrame,
        0x02 or 0x06 or 0x07 or 0x0E or 0x0F => FrameErrorLocation.Identifier,
        0x04 => FrameErrorLocation.SRTR,
        0x05 => FrameErrorLocation.IDE,
        0x0C => FrameErrorLocation.RTR,
        0x0B => FrameErrorLocation.DLC,
        0x0A => FrameErrorLocation.DataField,
        0x08 => FrameErrorLocation.CRCSequence,
        0x18 => FrameErrorLocation.CRCDelimiter,
        0x19 => FrameErrorLocation.AckSlot,
        0x1B => FrameErrorLocation.AckDelimiter,
        0x1A => FrameErrorLocation.EndOfFrame,
        0x12 => FrameErrorLocation.Intermission,
        0x11 => FrameErrorLocation.ActiveErrorFlag,
        0x16 => FrameErrorLocation.PassiveErrorFlag,
        0x17 => FrameErrorLocation.ErrorDelimiter,
        0x1C => FrameErrorLocation.OverloadFlag,
        0x13 => FrameErrorLocation.TolerateDominantBits,
        0x09 or 0x0D => FrameErrorLocation.Reserved,
        <= 0x1F => FrameErrorLocation.Unrecognized,
        _ => FrameErrorLocation.Invalid
    };

    private static FrameErrorType MapFlagsToType(ControlCanErrorFlag flags)
    {
        var t = FrameErrorType.None;
        if ((flags & ControlCanErrorFlag.BusOff) != 0) t |= FrameErrorType.BusOff;
        if ((flags & ControlCanErrorFlag.BusErr) != 0) t |= FrameErrorType.BusError;
        if ((flags & ControlCanErrorFlag.ArbitrationLost) != 0) t |= FrameErrorType.ArbitrationLost;
        if ((flags & (ControlCanErrorFlag.FifoOverflow | ControlCanErrorFlag.BufferOverflow | ControlCanErrorFlag.DeviceBufferOverflow)) != 0
            || (flags & (ControlCanErrorFlag.ErrPassive | ControlCanErrorFlag.ErrAlarm)) != 0)
            t |= FrameErrorType.Controller;
        if ((flags & (ControlCanErrorFlag.DeviceOpenErr | ControlCanErrorFlag.DeviceNotOpen | ControlCanErrorFlag.DeviceNotExist)) != 0)
            t |= FrameErrorType.DeviceError;
        if ((flags & ControlCanErrorFlag.LoadKernelErr) != 0) t |= FrameErrorType.DriverError;
        if ((flags & ControlCanErrorFlag.OutOfMemory) != 0) t |= FrameErrorType.ResourceError;
        if ((flags & ControlCanErrorFlag.CmdFailed) != 0) t |= FrameErrorType.CommandFailed;
        return t == FrameErrorType.None ? FrameErrorType.Unknown : t;
    }

    private static CanControllerStatus MapFlagsToController(ControlCanErrorFlag flags)
    {
        var cs = CanControllerStatus.None;
        if ((flags & (ControlCanErrorFlag.FifoOverflow | ControlCanErrorFlag.BufferOverflow | ControlCanErrorFlag.DeviceBufferOverflow)) != 0)
            cs |= CanControllerStatus.RxOverflow;
        return cs;
    }

    private static CanProtocolViolationType MapEccTypeToProt(byte eccType) => eccType switch
    {
        0 => CanProtocolViolationType.Bit,
        1 => CanProtocolViolationType.Form,
        2 => CanProtocolViolationType.Stuff,
        _ => CanProtocolViolationType.None,
    };

    public static void ThrowIfErr(uint status, string operation, ControlCanBus? bus = null, string? message = null)
    {
        if (status == StatusOk)
        {
            return;
        }
        CcApi.VCI_ERR_INFO? errorInfo = null;

        if (bus is not null)
        {
            try
            {
                CcApi.VCI_ReadErrInfo(bus.RawDevType, bus.DevIndex, bus.CanIndex, out var nativeInfo);
                errorInfo = nativeInfo;
            }
            catch (Exception ex)
            {
                CanKitLogger.LogWarning($"Failed to query channel error information for operation '{operation}'.", ex);
            }
        }
        message ??= $"ZLG native call '{operation}' failed with status {status}.";
        throw new ControlCanException(operation, message, status, errorInfo.HasValue ? ToErrorInfo(errorInfo.Value) : null);
    }
}

// Minimal copy of vendor error flags to avoid cross-adapter dependency
[Flags]
internal enum ControlCanErrorFlag : uint
{
    None = 0x0,
    FifoOverflow = 0x1,
    ErrAlarm = 0x2,
    ErrPassive = 0x4,
    ArbitrationLost = 0x8,
    BusErr = 0x10,
    BusOff = 0x20,
    BufferOverflow = 0x40,
    DeviceOpened = 0x100,
    DeviceOpenErr = 0x200,
    DeviceNotOpen = 0x400,
    DeviceBufferOverflow = 0x800,
    DeviceNotExist = 0x1000,
    LoadKernelErr = 0x2000,
    CmdFailed = 0x4000,
    OutOfMemory = 0x8000
}
