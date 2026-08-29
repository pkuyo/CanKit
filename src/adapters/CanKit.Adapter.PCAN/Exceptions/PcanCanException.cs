using System;
using CanKit.Core.Exceptions;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN.Exceptions;

public class PcanCanException : CanNativeCallException
{
    public PcanCanException(string operation, string message, PcanStatus status)
        : base(operation, message, (uint)status)
    {
        Status = status;
    }

    /// <summary>
    /// Native library could not be loaded (DLL missing, wrong bitness, or invalid image).
    /// </summary>
    public PcanCanException(string operation, string message, Exception innerException)
        : base(operation, message, CanKitErrorCode.NativeLibraryNotFound, nativeErrorCode: null, innerException)
    {
    }

    /// <summary>
    /// Wrap a load failure for PCAN-Basic (<c>PCANBasic</c> / <c>libpcanbasic</c>) or <c>PCAN-ISO-TP.dll</c>.
    /// </summary>
    public static PcanCanException NativeLibraryNotFound(
        string operation,
        string libraryName,
        string vendorRuntime,
        Exception innerException)
        => new(operation, NativeLibraryLoad.FormatMessage(libraryName, vendorRuntime, innerException), innerException);

    /// <summary>
    /// Pcan-basic status code. Unused when <see cref="CanKitException.ErrorCode"/> is
    /// <see cref="CanKitErrorCode.NativeLibraryNotFound"/>.
    /// </summary>
    public PcanStatus Status { get; }
}
