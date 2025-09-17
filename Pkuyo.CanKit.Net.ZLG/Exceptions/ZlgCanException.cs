using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.ZLG.Native;

namespace Pkuyo.CanKit.ZLG.Exceptions
{
    public class ZlgCanException : CanNativeCallException
    {
        public ZlgCanException(string operation, string message, uint statusCode,
            ZLGCAN.ZCAN_CHANNEL_ERROR_INFO? channelErrorInfo = null)
            : base(operation, message, statusCode)
        {
            StatusCode = statusCode;
            ChannelErrorInfo = channelErrorInfo;
        }

        public uint StatusCode { get; }

        public ZLGCAN.ZCAN_CHANNEL_ERROR_INFO? ChannelErrorInfo { get; }
    }
}
