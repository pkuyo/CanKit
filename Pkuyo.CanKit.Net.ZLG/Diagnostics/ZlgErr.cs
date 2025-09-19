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
            var rawEcc = errInfo.passive_ErrData[0];
            byte eccType   = (byte)((rawEcc >> 6) & 0x03);  // 0=Bit,1=Form,2=Stuff,3=Other
            byte eccDirBit = (byte)((rawEcc >> 5) & 0x01);  // 0=Tx, 1=Rx
            byte eccLoc    = (byte)(rawEcc & 0x1F);
                        
            var kind = eccType switch
            {
                0 => FrameErrorKind.BitError,
                1 => FrameErrorKind.FormError,
                2 => FrameErrorKind.StuffError,
                3 => InferOtherKindByLocation(eccLoc),      // 细分 CRC/ACK/Controller
                _ => FrameErrorKind.Unknown
            };


            var dir = eccDirBit switch
            {
                0 => FrameDirection.Tx,
                1 => FrameDirection.Rx,
                _ => FrameDirection.Unknown
            };
            return  new ZlgErrorInfo(errInfo.error_code)
            {
                Kind = kind,
                Direction = dir,
                SystemTimestamp = DateTime.Now
            };
            
            FrameErrorKind InferOtherKindByLocation(byte loc) => loc switch
            {
                0x08 or 0x09 => FrameErrorKind.CrcError,
                0x19 or 0x1A => FrameErrorKind.AckError,
                _            => FrameErrorKind.Controller
            };
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
