using System;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Diagnostics;

namespace Pkuyo.CanKit.Net.Core.Exceptions
{
    public enum CanKitErrorCode
    {
        Unknown = 0,

        DeviceOptionTypeMismatch = 1001,
        DeviceCreationFailed = 1002,
        DeviceDisposed = 1003,
        DeviceNotOpen = 1004,

        ChannelOptionTypeMismatch = 2001,
        ChannelCreationFailed = 2002,
        ChannelInitializationFailed = 2003,
        ChannelStartFailed = 2004,
        ChannelResetFailed = 2005,
        ChannelCleanBufferFailed = 2006,
        ChannelPollingFailed = 2007,
        ChannelDisposed = 2008,
        ChannelConfigurationInvalid = 2009,

        TransceiverMismatch = 3001,
        ProviderMismatch = 3002,
        FactoryDeviceMismatch = 3003,

        FilterConfigurationConflict = 4001,

        FeatureNotSupported = 5001,

        NativeCallFailed = 9000
    }

    public class CanKitException : Exception
    {
        public CanKitException(string message)
            : this(CanKitErrorCode.Unknown, message, null, null)
        {
        }

        public CanKitException(CanKitErrorCode errorCode, string message, uint? nativeErrorCode = null, Exception innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            NativeErrorCode = nativeErrorCode;
            CanKitLogger.LogException(this);
        }

        public CanKitErrorCode ErrorCode { get; }

        public uint? NativeErrorCode { get; }

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

    public class CanConfigurationException : CanKitException
    {
        public CanConfigurationException(CanKitErrorCode errorCode, string message)
            : base(errorCode, message)
        {
        }
    }

    public class CanOptionTypeMismatchException : CanConfigurationException
    {
        public CanOptionTypeMismatchException(CanKitErrorCode errorCode, Type expectedType, Type actualType, string scope)
            : base(errorCode,
                $"Expected {scope} options of type '{expectedType.FullName}', but got '{actualType.FullName}'.")
        {
            ExpectedType = expectedType;
            ActualType = actualType;
            Scope = scope;
        }

        public Type ExpectedType { get; }

        public Type ActualType { get; }

        public string Scope { get; }
    }

    public class CanFilterConfigurationException : CanConfigurationException
    {
        public CanFilterConfigurationException(string message)
            : base(CanKitErrorCode.FilterConfigurationConflict, message)
        {
        }
    }

    public class CanChannelConfigurationException : CanConfigurationException
    {
        public CanChannelConfigurationException(string message)
            : base(CanKitErrorCode.ChannelConfigurationInvalid, message)
        {
        }
    }

    public class CanDeviceException : CanKitException
    {
        public CanDeviceException(CanKitErrorCode errorCode, string message, Exception innerException = null)
            : base(errorCode, message, null, innerException)
        {
        }
    }

    public class CanDeviceDisposedException : CanDeviceException
    {
        public CanDeviceDisposedException()
            : base(CanKitErrorCode.DeviceDisposed, "The CAN device has been disposed and cannot be used anymore.")
        {
        }
    }

    public class CanDeviceNotOpenException : CanDeviceException
    {
        public CanDeviceNotOpenException()
            : base(CanKitErrorCode.DeviceNotOpen, "The CAN device must be opened before this operation can be performed.")
        {
        }
    }

    public class CanFactoryException : CanKitException
    {
        public CanFactoryException(CanKitErrorCode errorCode, string message)
            : base(errorCode, message)
        {
        }
    }

    public class CanChannelException : CanKitException
    {
        public CanChannelException(CanKitErrorCode errorCode, string message, uint? nativeErrorCode = null, Exception innerException = null)
            : base(errorCode, message, nativeErrorCode, innerException)
        {
        }
    }

    public class CanChannelCreationException : CanChannelException
    {
        public CanChannelCreationException(string message)
            : base(CanKitErrorCode.ChannelCreationFailed, message)
        {
        }
    }

    public class CanChannelDisposedException : CanChannelException
    {
        public CanChannelDisposedException()
            : base(CanKitErrorCode.ChannelDisposed, "The CAN channel has been disposed and cannot be used anymore.")
        {
        }
    }

    public class CanChannelInitializationException : CanChannelException
    {
        public CanChannelInitializationException(string message, uint? nativeErrorCode = null)
            : base(CanKitErrorCode.ChannelInitializationFailed, message, nativeErrorCode)
        {
        }
    }

    public class CanChannelStartException : CanChannelException
    {
        public CanChannelStartException(string message, uint? nativeErrorCode = null)
            : base(CanKitErrorCode.ChannelStartFailed, message, nativeErrorCode)
        {
        }
    }

    public class CanChannelResetException : CanChannelException
    {
        public CanChannelResetException(string message, uint? nativeErrorCode = null)
            : base(CanKitErrorCode.ChannelResetFailed, message, nativeErrorCode)
        {
        }
    }

    public class CanChannelCleanBufferException : CanChannelException
    {
        public CanChannelCleanBufferException(string message, uint? nativeErrorCode = null)
            : base(CanKitErrorCode.ChannelCleanBufferFailed, message, nativeErrorCode)
        {
        }
    }

    public class CanChannelPollingException : CanChannelException
    {
        public CanChannelPollingException(string message, Exception innerException = null)
            : base(CanKitErrorCode.ChannelPollingFailed, message, null, innerException)
        {
        }
    }

    public class CanTransceiverMismatchException : CanKitException
    {
        public CanTransceiverMismatchException(Type expectedType, Type actualType)
            : base(CanKitErrorCode.TransceiverMismatch,
                $"Transceiver type mismatch. Expected '{expectedType.FullName}', but got '{actualType.FullName}'.")
        {
            ExpectedType = expectedType;
            ActualType = actualType;
        }

        public Type ExpectedType { get; }

        public Type ActualType { get; }
    }

    public class CanProviderMismatchException : CanKitException
    {
        public CanProviderMismatchException(Type expectedType, Type actualType)
            : base(CanKitErrorCode.ProviderMismatch,
                $"Provider type mismatch. Expected '{expectedType.FullName}', but got '{actualType.FullName}'.")
        {
            ExpectedType = expectedType;
            ActualType = actualType;
        }

        public Type ExpectedType { get; }

        public Type ActualType { get; }
    }

    public class CanFactoryDeviceMismatchException : CanKitException
    {
        public CanFactoryDeviceMismatchException(Type expectedType, Type actualType)
            : base(CanKitErrorCode.FactoryDeviceMismatch,
                $"Factory expects device type '{expectedType.FullName}', but received '{actualType.FullName}'.")
        {
            ExpectedType = expectedType;
            ActualType = actualType;
        }

        public Type ExpectedType { get; }

        public Type ActualType { get; }
    }

    public class CanFeatureNotSupportedException : CanKitException
    {
        public CanFeatureNotSupportedException(CanFeature requestedFeature, CanFeature availableFeatures)
            : base(CanKitErrorCode.FeatureNotSupported,
                $"Feature '{requestedFeature}' is not supported by the current device. Available features: {availableFeatures}.")
        {
            RequestedFeature = requestedFeature;
            AvailableFeatures = availableFeatures;
        }

        public CanFeature RequestedFeature { get; }

        public CanFeature AvailableFeatures { get; }
    }

    public class CanNativeCallException : CanChannelException
    {
        public CanNativeCallException(string operation, string message, uint? nativeErrorCode = null, Exception innerException = null)
            : base(CanKitErrorCode.NativeCallFailed, $"{message} (Operation: {operation})", nativeErrorCode, innerException)
        {
            Operation = operation;
        }

        public string Operation { get; }
    }
}
