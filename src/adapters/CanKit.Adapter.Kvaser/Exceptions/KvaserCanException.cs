using System;
using CanKit.Core.Exceptions;
using CanKit.Adapter.Kvaser.Native;

namespace CanKit.Adapter.Kvaser.Exceptions;

/// <summary>
/// Exception for Kvaser CANlib native call failures.
/// </summary>
public sealed class KvaserCanException : CanNativeCallException
{
    public KvaserCanException(string operation, string message, Canlib.canStatus status)
        : base(operation, message, (uint)status)
    {
        Status = status;
    }

    /// <summary>
    /// Native library could not be loaded (DLL missing, wrong bitness, or invalid image).
    /// </summary>
    public KvaserCanException(string operation, string message, Exception innerException)
        : base(operation, message, CanKitErrorCode.NativeLibraryNotFound, nativeErrorCode: null, innerException)
    {
    }

    /// <summary>
    /// Wrap a <see cref="DllNotFoundException"/> or <see cref="BadImageFormatException"/> from canlib32.
    /// </summary>
    public static KvaserCanException NativeLibraryNotFound(string operation, Exception innerException)
        => new(operation, NativeLibraryLoad.FormatMessage("canlib32", "Kvaser CANlib", innerException), innerException);

    /// <summary>
    /// Kvaser CANlib status code. Unused when <see cref="CanKitException.ErrorCode"/> is
    /// <see cref="CanKitErrorCode.NativeLibraryNotFound"/>.
    /// </summary>
    public Canlib.canStatus Status { get; }
}
