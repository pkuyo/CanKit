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

    public int Status { get; }

    public string ErrorText { get; }
}

