using System;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.Vector.Diagnostics;

internal sealed class VectorNativeException : CanNativeCallException
{
    public VectorNativeException(string operation, int status, string errorText, string message)
        : base(operation, message, (uint)(status >= 0 ? status : unchecked((int)status)))
    {
        Status = status;
        ErrorText = errorText;
    }

    public VectorNativeException(string operation, string message, Exception innerException)
        : base(operation, message, CanKitErrorCode.NativeLibraryNotFound, nativeErrorCode: null, innerException)
    {
        ErrorText = innerException.Message;
    }

    public static VectorNativeException NativeLibraryNotFound(string operation, Exception innerException)
    {
        var library = Environment.Is64BitProcess ? "vxlapi64" : "vxlapi";
        return new VectorNativeException(
            operation,
            NativeLibraryLoad.FormatMessage(library, "Vector XL Driver Library", innerException),
            innerException);
    }

    public int Status { get; }

    public string ErrorText { get; }
}
