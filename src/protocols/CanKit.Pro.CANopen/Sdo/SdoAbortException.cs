using System;
using CanKit.Core.Exceptions;

namespace CanKit.Pro.CANopen.Sdo;

/// <summary>
/// Thrown when an SDO transfer terminates with an Abort frame (CiA 301 §7.2.4.3.17). Carries
/// the failing (<see cref="Index"/>, <see cref="Subindex"/>) and the raw 32-bit
/// <see cref="AbortCode"/> — matched against <see cref="SdoAbortCode"/> when possible.
/// Derives from <see cref="CanKitException"/> so L2/L3/L4 failures can be caught uniformly
/// across the library (NFR-006 error architecture, arc42 ADR-12).
/// </summary>
/// <remarks>
/// Both the client and the server produce this exception. On the client side, the exception
/// wraps a peer-sent Abort frame (or a locally-triggered timeout/abort). On the server side,
/// the abort is emitted onto the bus and the exception is used only for internal control flow.
/// </remarks>
public sealed class SdoAbortException : CanKitException
{
    /// <summary>Object index that the failing SDO transfer targeted.</summary>
    public ushort Index { get; }

    /// <summary>Object subindex that the failing SDO transfer targeted.</summary>
    public byte Subindex { get; }

    /// <summary>Raw 32-bit abort code (little-endian in the wire frame).</summary>
    public uint AbortCode { get; }

    /// <summary>Constructs a new abort exception with an explicit message.</summary>
    public SdoAbortException(ushort index, byte subindex, uint abortCode, string message)
        : base(CanKitErrorCode.ProtocolPeerAbort, message)
    {
        Index = index;
        Subindex = subindex;
        AbortCode = abortCode;
    }

    /// <summary>Constructs a new abort exception with a message derived from
    /// <paramref name="abortCode"/>.</summary>
    public SdoAbortException(ushort index, byte subindex, SdoAbortCode abortCode)
        : this(index, subindex, (uint)abortCode,
            $"SDO transfer for 0x{index:X4}:{subindex:X2} aborted with code 0x{(uint)abortCode:X8} ({abortCode}).")
    {
    }
}
