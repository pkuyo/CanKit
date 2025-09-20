using System;
using System.Runtime.InteropServices;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Diagnostics;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Exceptions;
using Pkuyo.CanKit.ZLG.Native;

namespace Pkuyo.CanKit.ZLG.Diagnostics
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
                    var nativeInfo = new ZLGCAN.ZCAN_CHANNEL_ERROR_INFO();
                    ZLGCAN.ZCAN_ReadChannelErrInfo(handle, ref nativeInfo);
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
            byte eccType   = (byte)((rawEcc >> 6) & 0x03);  // 0=Bit,1=Form,2=Stuff,3=Other
            byte eccDirBit = (byte)((rawEcc >> 5) & 0x01);  // 0=Tx, 1=Rx
            byte eccLoc    = (byte)(rawEcc & 0x1F);

            FrameErrorKind eccKind = eccType switch
            {
                0 => FrameErrorKind.BitError,
                1 => FrameErrorKind.FormError,
                2 => FrameErrorKind.StuffError,
                3 => InferOtherKindByLocation(eccLoc),
                _ => FrameErrorKind.Unknown
            };

            var dir = eccDirBit switch
            {
                0 => FrameDirection.Tx,
                1 => FrameDirection.Rx,
                _ => FrameDirection.Unknown
            };

            // Map ZLG error flags to more granular kinds
            var flags = (ZlgErrorFlag)errInfo.error_code;
            var kind = MapZlgFlagsToKind(flags, eccKind);

            return new ZlgErrorInfo(errInfo.error_code)
            {
                Kind = kind,
                Direction = dir,
                SystemTimestamp = DateTime.Now
            };

            static FrameErrorKind InferOtherKindByLocation(byte loc) => loc switch
            {
                0x08 or 0x09 => FrameErrorKind.CrcError,
                0x19 or 0x1A => FrameErrorKind.AckError,
                _            => FrameErrorKind.Controller
            };

            static FrameErrorKind MapZlgFlagsToKind(ZlgErrorFlag flags, FrameErrorKind fallback)
            {
                if (flags == ZlgErrorFlag.None)
                    return fallback;

                // Channel/bus states
                if ((flags & ZlgErrorFlag.BusOff) != 0)
                    fallback |= FrameErrorKind.BusOff;
                if ((flags & ZlgErrorFlag.BusErr) != 0)
                    fallback |= FrameErrorKind.BusError;
                if ((flags & ZlgErrorFlag.ArbitrationLost) != 0)
                    fallback |= FrameErrorKind.ArbitrationLost;
                if ((flags & (ZlgErrorFlag.FifoOverflow | ZlgErrorFlag.BufferOverflow | ZlgErrorFlag.DeviceBufferOverflow)) != 0)
                    fallback |= FrameErrorKind.RxOverflow;
                if ((flags & ZlgErrorFlag.ErrPassive) != 0)
                    fallback |= FrameErrorKind.Passive;
                if ((flags & ZlgErrorFlag.ErrAlarm) != 0)
                    fallback |= FrameErrorKind.Warning;

                // Device/system classes
                if ((flags & (ZlgErrorFlag.DeviceOpenErr | ZlgErrorFlag.DeviceNotOpen | ZlgErrorFlag.DeviceNotExist)) != 0)
                    fallback |= FrameErrorKind.DeviceError;
                if ((flags & ZlgErrorFlag.LoadKernelErr) != 0)
                    fallback |= FrameErrorKind.DriverError;
                if ((flags & ZlgErrorFlag.OutOfMemory) != 0)
                    fallback |= FrameErrorKind.ResourceError;
                if ((flags & ZlgErrorFlag.CmdFailed) != 0)
                    fallback |= FrameErrorKind.CommandFailed;

                return fallback;
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


