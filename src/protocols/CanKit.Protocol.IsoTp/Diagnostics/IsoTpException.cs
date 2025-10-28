using CanKit.Protocol.IsoTp.Defines;

namespace CanKit.Protocol.IsoTp.Diagnostics;

public enum IsoTpErrorCode
{
    None = 0,

    Timeout_N_Bs,
    Timeout_N_Cs,
    Timeout_N_Cr,
    Timeout_N_Ar,
    Timeout_N_As,
    Timeout_Overall,
    SequenceError, LengthMismatch, PaddingError, UnexpectedPci,
    Remote_Overflow,
    Local_Overflow,
    Busy, Disposed, Cancelled,

    BusTxRejected, BusDriverError, BackgroundException,
}

public sealed class IsoTpException : Exception
{
    public IsoTpErrorCode Code { get; }
    public IsoTpEndpoint? Endpoint { get; }

    public IsoTpException(IsoTpErrorCode code, string? message = null, IsoTpEndpoint? ep = null, Exception? inner = null)
        : base(message, inner)
    { Code = code; Endpoint = ep; }
}
