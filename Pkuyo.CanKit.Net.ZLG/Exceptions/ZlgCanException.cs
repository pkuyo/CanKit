using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Native;

namespace Pkuyo.CanKit.ZLG.Exceptions
{
    public class ZlgCanException : CanNativeCallException
    {
        public ZlgCanException(string operation, string message, uint statusCode,
            ZlgErrorInfo? channelErrorInfo = null)
            : base(operation, message, statusCode)
        {
            StatusCode = statusCode;
            ChannelErrorInfo = channelErrorInfo;
        }

        public uint StatusCode { get; }

        public ZlgErrorInfo? ChannelErrorInfo { get; }
    }
    
    public class ZlgFeatureNotSupportedException : CanKitException
    {
        public ZlgFeatureNotSupportedException(ZlgFeature requestedFeature, ZlgFeature availableFeatures)
            : base(CanKitErrorCode.FeatureNotSupported,
                $"ZLG Feature '{requestedFeature}' is not supported by the current device. Available features: {availableFeatures}.")
        {
            RequestedFeature = requestedFeature;
            AvailableFeatures = availableFeatures;
        }

        public ZlgFeature RequestedFeature { get; }

        public ZlgFeature AvailableFeatures { get; }
    }
}
