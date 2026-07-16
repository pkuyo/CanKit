using System;

namespace CanKit.Pro.J1939Tp;

/// <summary>Base class for every exception raised by the J1939-TP channel.</summary>
public class J1939TpException : Exception
{
    /// <summary>Creates a new <see cref="J1939TpException"/>.</summary>
    public J1939TpException(string message) : base(message) { }

    /// <summary>Creates a new <see cref="J1939TpException"/> wrapping an underlying exception.</summary>
    public J1939TpException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Raised when a J1939-21 §5.10.5 Connection Abort is issued or received on a TP.CM session,
/// or when a BAM/TP.CM session gives up because one of T1/T2/T3/T4/Tr/Th expired.
/// </summary>
public sealed class J1939TpAbortException : J1939TpException
{
    /// <summary>Creates a new <see cref="J1939TpAbortException"/>.</summary>
    public J1939TpAbortException(J1939TpAbortReason reason, uint pgn, string message)
        : base(message)
    {
        Reason = reason;
        Pgn = pgn;
    }

    /// <summary>The J1939-21 §5.10.5 abort reason code carried on the wire.</summary>
    public J1939TpAbortReason Reason { get; }

    /// <summary>The data PGN of the aborted session.</summary>
    public uint Pgn { get; }
}

/// <summary>Raised when the CAN driver rejects an outbound TP.CM / TP.DT frame.</summary>
public sealed class J1939TpSendRejectedException : J1939TpException
{
    /// <summary>Creates a new <see cref="J1939TpSendRejectedException"/>.</summary>
    public J1939TpSendRejectedException(string message) : base(message) { }
}
