using System;
using Pkuyo.CanKit.Net.Core.Diagnostics;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Exceptions;
using Pkuyo.CanKit.ZLG.Native;

namespace Pkuyo.CanKit.ZLG.Diagnostics
{
    public static class ZlgErr
    {
        public const uint StatusOk = 1;

        public static void ThrowIfError(uint status, string operation, ZlgChannelHandle channelHandle = null, string message = null)
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
            throw new ZlgCanException(operation, message, status, errorInfo);
        }

        public static void ThrowIfInvalid(ZlgChannelHandle handle, string operation)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (!handle.IsInvalid)
            {
                return;
            }

            throw new ZlgCanException(operation, $"ZLG native call '{operation}' returned an invalid handle.", 0);
        }
    }
}
