using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Native;

namespace Pkuyo.CanKit.ZLG.Exceptions
{
    /// <summary>
    /// Exception for ZLG native call failures (ZLG 原生调用失败异常)。
    /// </summary>
    public class ZlgCanException : CanNativeCallException
    {
        /// <summary>
        /// Create ZLG exception with operation, message and status (构造包含操作、消息与状态码的异常)。
        /// </summary>
        /// <param name="operation">Operation causing the exception (出错的操作)。</param>
        /// <param name="message">Low-level error message (底层错误消息)。</param>
        /// <param name="statusCode">ZLG status code (ZLG 状态码)。</param>
        /// <param name="channelErrorInfo">Optional channel error info (可选通道错误信息)。</param>
        public ZlgCanException(string operation, string message, uint statusCode,
            ZlgErrorInfo channelErrorInfo = null)
            : base(operation, message, statusCode)
        {
            StatusCode = statusCode;
            ChannelErrorInfo = channelErrorInfo;
        }

        /// <summary>
        /// ZLG API native status code (ZLG 原生状态码)。
        /// </summary>
        public uint StatusCode { get; }

        /// <summary>
        /// Channel error info for diagnostics (通道错误详情)。
        /// </summary>
        public ZlgErrorInfo ChannelErrorInfo { get; }
    }

    /// <summary>
    /// Thrown when a requested ZLG feature is not supported (请求的 ZLG 功能不被支持时抛出)。
    /// </summary>
    public class ZlgFeatureNotSupportedException : CanKitException
    {
        /// <summary>
        /// Create exception with requested/available features (构造包含请求/可用功能集的异常)。
        /// </summary>
        public ZlgFeatureNotSupportedException(ZlgFeature requestedFeature, ZlgFeature availableFeatures)
            : base(CanKitErrorCode.FeatureNotSupported,
                $"ZLG Feature '{requestedFeature}' is not supported by the current device. Available features: {availableFeatures}.")
        {
            RequestedFeature = requestedFeature;
            AvailableFeatures = availableFeatures;
        }

        /// <summary>
        /// Requested feature set (请求的功能集)。
        /// </summary>
        public ZlgFeature RequestedFeature { get; }

        /// <summary>
        /// Available feature set (设备当前支持的功能集)。
        /// </summary>
        public ZlgFeature AvailableFeatures { get; }
    }
}

