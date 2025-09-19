using System;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Diagnostics;

namespace Pkuyo.CanKit.Net.Core.Exceptions
{
    /// <summary>
    /// Unified error codes across the library (库内统一错误码)。
    /// </summary>
    public enum CanKitErrorCode
    {
        /// <summary>
        /// Unknown error (未知错误)。
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Device option type mismatch (设备选项类型不匹配)。
        /// </summary>
        DeviceOptionTypeMismatch = 1001,
        /// <summary>
        /// Device creation failed (设备实例创建失败)。
        /// </summary>
        DeviceCreationFailed = 1002,
        /// <summary>
        /// Device disposed and cannot be used (设备已释放，无法再使用)。
        /// </summary>
        DeviceDisposed = 1003,
        /// <summary>
        /// Device is not open (设备未打开)。
        /// </summary>
        DeviceNotOpen = 1004,

        /// <summary>
        /// Channel option type mismatch (通道选项类型不匹配)。
        /// </summary>
        ChannelOptionTypeMismatch = 2001,
        /// <summary>
        /// Channel creation failed (通道创建失败)。
        /// </summary>
        ChannelCreationFailed = 2002,
        /// <summary>
        /// Channel initialization failed (通道初始化失败)。
        /// </summary>
        ChannelInitializationFailed = 2003,
        /// <summary>
        /// Channel start failed (通道启动失败)。
        /// </summary>
        ChannelStartFailed = 2004,
        /// <summary>
        /// Channel reset failed (通道复位失败)。
        /// </summary>
        ChannelResetFailed = 2005,
        /// <summary>
        /// Channel clean buffer failed (清理缓冲失败)。
        /// </summary>
        ChannelCleanBufferFailed = 2006,
        /// <summary>
        /// Channel polling failed (通道轮询失败)。
        /// </summary>
        ChannelPollingFailed = 2007,
        /// <summary>
        /// Channel disposed (通道已释放)。
        /// </summary>
        ChannelDisposed = 2008,
        /// <summary>
        /// Channel configuration invalid (通道配置无效/冲突)。
        /// </summary>
        ChannelConfigurationInvalid = 2009,
        /// <summary>
        /// Channel is not open (通道未打开)。
        /// </summary>
        ChannelNotOpen = 2010,

        /// <summary>
        /// Transceiver type mismatch (收发器类型不匹配)。
        /// </summary>
        TransceiverMismatch = 3001,
        /// <summary>
        /// Provider type mismatch (提供者类型不匹配)。
        /// </summary>
        ProviderMismatch = 3002,
        /// <summary>
        /// Factory device type mismatch (工厂期望设备类型不匹配)。
        /// </summary>
        FactoryDeviceMismatch = 3003,

        /// <summary>
        /// Filter configuration conflict (过滤配置冲突)。
        /// </summary>
        FilterConfigurationConflict = 4001,

        /// <summary>
        /// Feature not supported by device (设备不支持该功能)。
        /// </summary>
        FeatureNotSupported = 5001,

        /// <summary>
        /// Native call failed with vendor status (原生调用失败，带返回状态)。
        /// </summary>
        NativeCallFailed = 9000,
    }

    /// <summary>
    /// Base exception with error codes and optional native code (带错误码和可选原生码的基础异常)。
    /// </summary>
    public class CanKitException : Exception
    {
        /// <summary>
        /// Create with default code <see cref="CanKitErrorCode.Unknown"/> (使用默认未知错误码创建)。
        /// </summary>
        public CanKitException(string message)
            : this(CanKitErrorCode.Unknown, message, null, null)
        {
        }

        /// <summary>
        /// Create with code, message, optional native code and inner exception (使用错误码/消息/可选原生码/内部异常创建)。
        /// </summary>
        public CanKitException(CanKitErrorCode errorCode, string message, uint? nativeErrorCode = null, Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            NativeErrorCode = nativeErrorCode;
            CanKitLogger.LogException(this);
        }

        /// <summary>
        /// Library-level error code (库级错误码)。
        /// </summary>
        public CanKitErrorCode ErrorCode { get; }

        /// <summary>
        /// Optional native/vendor error code (可选原生/厂商错误码)。
        /// </summary>
        public uint? NativeErrorCode { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            var baseString = base.ToString();
            var codePart = $"[{ErrorCode}] {baseString}";

            if (NativeErrorCode.HasValue)
            {
                codePart += $" (NativeErrorCode: {NativeErrorCode.Value})";
            }

            return codePart;
        }
    }

    /// <summary>
    /// Base type for configuration stage exceptions (配置阶段异常基类)。
    /// </summary>
    public class CanConfigurationException : CanKitException
    {
        /// <summary>
        /// Create with specific code and message (使用指定错误码与消息创建)。
        /// </summary>
        public CanConfigurationException(CanKitErrorCode errorCode, string message)
            : base(errorCode, message)
        {
        }
    }

    /// <summary>
    /// Option type mismatch during configuration (配置选项类型不匹配)。
    /// </summary>
    public class CanOptionTypeMismatchException : CanConfigurationException
    {
        /// <summary>
        /// Create with expected/actual types and scope (包含期望/实际类型和作用域)。
        /// </summary>
        public CanOptionTypeMismatchException(CanKitErrorCode errorCode, Type expectedType, Type actualType, string scope)
            : base(errorCode,
                $"Expected {scope} options of type '{expectedType.FullName}', but got '{actualType.FullName}'.")
        {
            ExpectedType = expectedType;
            ActualType = actualType;
            Scope = scope;
        }

        /// <summary>
        /// Expected type (期望类型)。
        /// </summary>
        public Type ExpectedType { get; }

        /// <summary>
        /// Actual type (实际类型)。
        /// </summary>
        public Type ActualType { get; }

        /// <summary>
        /// Scope where the mismatch occurred (类型不匹配所在作用域)。
        /// </summary>
        public string Scope { get; }
    }

    /// <summary>
    /// Thrown when filter configuration conflicts (过滤配置冲突异常)。
    /// </summary>
    public class CanFilterConfigurationException : CanConfigurationException
    {
        public CanFilterConfigurationException(string message)
            : base(CanKitErrorCode.FilterConfigurationConflict, message)
        {
        }
    }

    /// <summary>
    /// Thrown when channel configuration is invalid (通道配置无效异常)。
    /// </summary>
    public class CanChannelConfigurationException : CanConfigurationException
    {
        public CanChannelConfigurationException(string message)
            : base(CanKitErrorCode.ChannelConfigurationInvalid, message)
        {
        }
    }

    /// <summary>
    /// Base type for device-related exceptions (设备相关异常基类)。
    /// </summary>
    public class CanDeviceException : CanKitException
    {
        public CanDeviceException(CanKitErrorCode errorCode, string message, Exception? innerException = null)
            : base(errorCode, message, null, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when device has been disposed (设备已释放异常)。
    /// </summary>
    public class CanDeviceDisposedException : CanDeviceException
    {
        public CanDeviceDisposedException()
            : base(CanKitErrorCode.DeviceDisposed, "The CAN device has been disposed and cannot be used anymore.")
        {
        }
    }

    /// <summary>
    /// Thrown when device is not open (设备未打开异常)。
    /// </summary>
    public class CanDeviceNotOpenException : CanDeviceException
    {
        public CanDeviceNotOpenException()
            : base(CanKitErrorCode.DeviceNotOpen, "The CAN device must be opened before this operation can be performed.")
        {
        }
    }

    /// <summary>
    /// Factory-related exception (工厂相关异常)。
    /// </summary>
    public class CanFactoryException : CanKitException
    {
        public CanFactoryException(CanKitErrorCode errorCode, string message)
            : base(errorCode, message)
        {
        }
    }

    /// <summary>
    /// Base type for channel-related exceptions (通道相关异常基类)。
    /// </summary>
    public class CanChannelException : CanKitException
    {
        public CanChannelException(CanKitErrorCode errorCode, string message, uint? nativeErrorCode = null, Exception? innerException = null)
            : base(errorCode, message, nativeErrorCode, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when channel creation fails (通道创建失败异常)。
    /// </summary>
    public class CanChannelCreationException : CanChannelException
    {
        public CanChannelCreationException(string message)
            : base(CanKitErrorCode.ChannelCreationFailed, message)
        {
        }
    }

    /// <summary>
    /// Thrown when channel has been disposed (通道已释放异常)。
    /// </summary>
    public class CanChannelDisposedException : CanChannelException
    {
        public CanChannelDisposedException()
            : base(CanKitErrorCode.ChannelDisposed, "The CAN channel has been disposed and cannot be used anymore.")
        {
        }
    }

    /// <summary>
    /// Thrown when channel is not open (通道未打开异常)。
    /// </summary>
    public class CanChannelNotOpenException : CanDeviceException
    {
        public CanChannelNotOpenException()
            : base(CanKitErrorCode.ChannelNotOpen, "The CAN channel must be opened before this operation can be performed.")
        {
        }
    }

    /// <summary>
    /// Transceiver type mismatch (收发器类型不匹配异常)。
    /// </summary>
    public class CanTransceiverMismatchException : CanKitException
    {
        public CanTransceiverMismatchException(Type expectedType, Type actualType)
            : base(CanKitErrorCode.TransceiverMismatch,
                $"Transceiver type mismatch. Expected '{expectedType.FullName}', but got '{actualType.FullName}'.")
        {
            ExpectedType = expectedType;
            ActualType = actualType;
        }

        /// <summary>
        /// Expected transceiver type (期望的收发器类型)。
        /// </summary>
        public Type ExpectedType { get; }

        /// <summary>
        /// Actual transceiver type (实际收发器类型)。
        /// </summary>
        public Type ActualType { get; }
    }

    /// <summary>
    /// Provider type mismatch (提供者类型不匹配异常)。
    /// </summary>
    public class CanProviderMismatchException : CanKitException
    {
        public CanProviderMismatchException(Type expectedType, Type actualType)
            : base(CanKitErrorCode.ProviderMismatch,
                $"Provider type mismatch. Expected '{expectedType.FullName}', but got '{actualType.FullName}'.")
        {
            ExpectedType = expectedType;
            ActualType = actualType;
        }

        /// <summary>
        /// Expected provider type (期望的提供者类型)。
        /// </summary>
        public Type ExpectedType { get; }

        /// <summary>
        /// Actual provider type (实际提供者类型)。
        /// </summary>
        public Type ActualType { get; }
    }

    /// <summary>
    /// Factory produced device type mismatch (工厂产出设备类型不匹配异常)。
    /// </summary>
    public class CanFactoryDeviceMismatchException : CanKitException
    {
        public CanFactoryDeviceMismatchException(Type expectedType, Type actualType)
            : base(CanKitErrorCode.FactoryDeviceMismatch,
                $"Factory expects device type '{expectedType.FullName}', but received '{actualType.FullName}'.")
        {
            ExpectedType = expectedType;
            ActualType = actualType;
        }

        /// <summary>
        /// Expected device type (期望的设备类型)。
        /// </summary>
        public Type ExpectedType { get; }

        /// <summary>
        /// Actual device type (实际设备类型)。
        /// </summary>
        public Type ActualType { get; }
    }

    /// <summary>
    /// Requested feature not supported by device (请求功能不被设备支持异常)。
    /// </summary>
    public class CanFeatureNotSupportedException : CanKitException
    {
        public CanFeatureNotSupportedException(CanFeature requestedFeature, CanFeature availableFeatures)
            : base(CanKitErrorCode.FeatureNotSupported,
                $"Feature '{requestedFeature}' is not supported by the current device. Available features: {availableFeatures}|{(int)availableFeatures}.")
        {
            RequestedFeature = requestedFeature;
            AvailableFeatures = availableFeatures;
        }

        /// <summary>
        /// Requested feature set (请求的功能集)。
        /// </summary>
        public CanFeature RequestedFeature { get; }

        /// <summary>
        /// Available feature set (设备当前支持的功能集)。
        /// </summary>
        public CanFeature AvailableFeatures { get; }
    }

    /// <summary>
    /// Encapsulates native call failure with low-level info (封装原生调用失败并携带底层信息)。
    /// </summary>
    public class CanNativeCallException : CanChannelException
    {
        public CanNativeCallException(string operation, string message, uint? nativeErrorCode = null, Exception? innerException = null)
            : base(CanKitErrorCode.NativeCallFailed, $"{message} (Operation: {operation})", nativeErrorCode, innerException)
        {
            Operation = operation;
        }

        /// <summary>
        /// Operation name that failed (失败的操作名称)。
        /// </summary>
        public string Operation { get; }
    }
}

