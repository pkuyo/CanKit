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
    /// Kvaser CANlib status code.
    /// </summary>
    public Canlib.canStatus Status { get; }
}
