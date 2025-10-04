using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Exceptions;
using CanKit.Adapter.ZLG.Native;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Utils;

namespace CanKit.Adapter.ZLG.Diagnostics
{
    public static class ZlgErr
    {
        public const uint StatusOk = 1;

        public static void ThrowIfError(uint status, string operation, ZlgChannelHandle? channelHandle = null, string? message = null)
        {
            if (status == StatusOk)
            {
                return;
            }

            ZLGCAN.ZCAN_CHANNEL_ERROR_INFO? errorInfo = null;

            if (channelHandle is { IsInvalid: false } handle)
            {
                try
                {
                    ZLGCAN.ZCAN_ReadChannelErrInfo(handle, out var nativeInfo);
                    errorInfo = nativeInfo;
                }
                catch (Exception ex)
                {
                    CanKitLogger.LogWarning($"Failed to query channel error information for operation '{operation}'.", ex);
                }
            }

            message ??= $"ZLG native call '{operation}' failed with status {status}.";
            throw new ZlgCanException(operation, message, status, errorInfo.HasValue ? ToErrorInfo(errorInfo.Value) : null);
        }

        public static ZlgErrorInfo ToErrorInfo(ZLGCAN.ZCAN_CHANNEL_ERROR_INFO errInfo)
        {
            // ECC fields
            var rawEcc = errInfo.passive_ErrData[0];
            byte eccType = (byte)((rawEcc >> 6) & 0x03);  // 0=Bit,1=Form,2=Stuff,3=Other
            byte eccDirBit = (byte)((rawEcc >> 5) & 0x01);  // 0=Tx, 1=Rx
            byte eccLoc = (byte)(rawEcc & 0x1F);

            int rec = errInfo.passive_ErrData[1];
            int tec = errInfo.passive_ErrData[2];

            var dir = eccDirBit switch
            {
                0 => FrameDirection.Tx,
                1 => FrameDirection.Rx,
                _ => FrameDirection.Unknown
            };

            // Map ZLG error flags to top-level classes
            var flags = (ZlgErrorFlag)errInfo.error_code;
            var type = MapZlgFlagsToType(flags);
            var loc = MapZlgEccLocToLocation(eccLoc);

            var ctrl = MapZlgFlagsToController(flags) | CanKitExtension.ToControllerStatus(rec, tec);
            var prot = MapEccTypeToProt(eccType);

            // Protocol violation presence inferred from ECC type; if ECC indicates ACK location in "Other",
            // surface it as AckError (top-level) instead of ProtocolViolation.
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

            byte? arbBit = ((flags & ZlgErrorFlag.ArbitrationLost) != 0 && errInfo.arLost_ErrData < 32)
                ? errInfo.arLost_ErrData
                : null;
            return new ZlgErrorInfo(errInfo.error_code)
            {
                Type = type,
                ControllerStatus = ctrl,
                ProtocolViolation = prot,
                ProtocolViolationLocation = loc,
                Direction = dir,
                TransceiverStatus = CanTransceiverStatus.Unspecified,
                SystemTimestamp = DateTime.Now,
                ArbitrationLostBit = arbBit,
                ErrorCounters = new CanErrorCounters()
                {
                    TransmitErrorCounter = tec,
                    ReceiveErrorCounter = rec,
                }
            };

            static FrameErrorLocation MapZlgEccLocToLocation(byte loc) => loc switch
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

            static FrameErrorType MapZlgFlagsToType(ZlgErrorFlag flags)
            {
                var t = FrameErrorType.None;

                // Channel/bus states
                if ((flags & ZlgErrorFlag.BusOff) != 0)
                    t |= FrameErrorType.BusOff;
                if ((flags & ZlgErrorFlag.BusErr) != 0)
                    t |= FrameErrorType.BusError;
                if ((flags & ZlgErrorFlag.ArbitrationLost) != 0)
                    t |= FrameErrorType.ArbitrationLost;
                if ((flags & (ZlgErrorFlag.FifoOverflow | ZlgErrorFlag.BufferOverflow | ZlgErrorFlag.DeviceBufferOverflow)) != 0
                    || (flags & (ZlgErrorFlag.ErrPassive | ZlgErrorFlag.ErrAlarm)) != 0)
                    t |= FrameErrorType.Controller;

                // Device/system classes
                if ((flags & (ZlgErrorFlag.DeviceOpenErr | ZlgErrorFlag.DeviceNotOpen | ZlgErrorFlag.DeviceNotExist)) != 0)
                    t |= FrameErrorType.DeviceError;
                if ((flags & ZlgErrorFlag.LoadKernelErr) != 0)
                    t |= FrameErrorType.DriverError;
                if ((flags & ZlgErrorFlag.OutOfMemory) != 0)
                    t |= FrameErrorType.ResourceError;
                if ((flags & ZlgErrorFlag.CmdFailed) != 0)
                    t |= FrameErrorType.CommandFailed;

                if (t == FrameErrorType.None)
                    t = FrameErrorType.Unknown;
                return t;
            }

            static CanControllerStatus MapZlgFlagsToController(ZlgErrorFlag flags)
            {
                var cs = CanControllerStatus.None;
                if ((flags & (ZlgErrorFlag.FifoOverflow | ZlgErrorFlag.BufferOverflow | ZlgErrorFlag.DeviceBufferOverflow)) != 0)
                    cs |= CanControllerStatus.RxOverflow;
                return cs;
            }

            static CanProtocolViolationType MapEccTypeToProt(byte eccType)
            {
                return eccType switch
                {
                    0 => CanProtocolViolationType.Bit,
                    1 => CanProtocolViolationType.Form,
                    2 => CanProtocolViolationType.Stuff,
                    _ => CanProtocolViolationType.None,
                };
            }
        }

        public static void ThrowIfInvalid(SafeHandle handle, string operation)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (!handle.IsInvalid)
            {
                return;
            }

            throw new ZlgCanException(operation, $"ZLG native call '{operation}' returned an invalid handle.", 0);
        }

        public static void ThrowIfNotSupport(ZlgFeature deviceFeatures, ZlgFeature checkFeature)
        {
            if ((deviceFeatures & checkFeature) == 0U)
                throw new ZlgFeatureNotSupportedException(checkFeature, deviceFeatures);
        }
    }
}
