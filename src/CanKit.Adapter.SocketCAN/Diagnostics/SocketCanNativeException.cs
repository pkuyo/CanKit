using CanKit.Adapter.SocketCAN.Native;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.SocketCAN.Diagnostics;

public class SocketCanNativeException(
    string operation,
    string message,
    uint nativeErrorCode,
    Exception? innerException = null)
    : CanNativeCallException(operation, message, nativeErrorCode, innerException)
{
    public string ErrMessage { get; } = Libc.StrError((int)nativeErrorCode);
}
