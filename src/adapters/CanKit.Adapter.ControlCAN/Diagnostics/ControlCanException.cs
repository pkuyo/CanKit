using System;
using CanKit.Abstractions.API.Can;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.ControlCAN.Diagnostics;

/// <summary>
/// Exception for ControlCAN native call failures (ControlCAN 原生调用失败异常)。
/// </summary>
public sealed class ControlCanException : CanNativeCallException
{
    public ControlCanException(string operation, string message, uint statusCode,
        ICanErrorInfo? channelErrorInfo = null)
        : base(operation, message, statusCode)
    {
        StatusCode = statusCode;
        ChannelErrorInfo = channelErrorInfo;
    }

    /// <summary>
    /// Native library could not be loaded (DLL missing, wrong bitness, or invalid image).
    /// </summary>
    public ControlCanException(string operation, string message, Exception innerException)
        : base(operation, message, CanKitErrorCode.NativeLibraryNotFound, nativeErrorCode: null, innerException)
    {
    }

    /// <summary>
    /// Wrap a <see cref="DllNotFoundException"/> or <see cref="BadImageFormatException"/> from controlcan.
    /// </summary>
    public static ControlCanException NativeLibraryNotFound(string operation, Exception innerException)
        => new(operation, NativeLibraryLoad.FormatMessage("controlcan", "ControlCAN vendor driver", innerException), innerException);

    /// <summary>
    /// ControlCAN native status code. Unused when <see cref="CanKitException.ErrorCode"/> is
    /// <see cref="CanKitErrorCode.NativeLibraryNotFound"/>.
    /// </summary>
    public uint StatusCode { get; }

    public ICanErrorInfo? ChannelErrorInfo { get; }
}
