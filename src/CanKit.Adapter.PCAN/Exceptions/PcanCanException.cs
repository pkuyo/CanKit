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
    /// Pcan-basic status code.
    /// </summary>
    public PcanStatus Status { get; }
}
