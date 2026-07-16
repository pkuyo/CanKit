using System;

namespace CanKit.Pro.J1939Tp;

/// <summary>
/// One fully reassembled J1939 multi-packet PDU as delivered by <see cref="IJ1939TpChannel"/>.
/// Carries the transport-service context every J1939 application layer needs to route the
/// message: the PGN of the announced data, the source address of the transmitter, the
/// destination address (0xFF for BAM, the local address for TP.CM), and whether it was carried
/// by TP.BAM or TP.CM.
/// </summary>
public readonly struct J1939TpDatagram
{
    /// <summary>Creates a new <see cref="J1939TpDatagram"/>.</summary>
    public J1939TpDatagram(uint pgn, byte sourceAddress, byte destinationAddress,
        J1939TpKind kind, byte[] payload)
    {
        Pgn = pgn;
        SourceAddress = sourceAddress;
        DestinationAddress = destinationAddress;
        Kind = kind;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    /// <summary>The J1939 data PGN announced by the transport session (from RTS/BAM bytes 5..7).</summary>
    public uint Pgn { get; }

    /// <summary>Source address of the transmitting node.</summary>
    public byte SourceAddress { get; }

    /// <summary>Destination address (0xFF for BAM; local SA for TP.CM).</summary>
    public byte DestinationAddress { get; }

    /// <summary>Which J1939-21 §5.10 transport flavor delivered the PDU.</summary>
    public J1939TpKind Kind { get; }

    /// <summary>The reassembled payload bytes (owned by the receiver; not aliased to any frame buffer).</summary>
    public byte[] Payload { get; }
}

/// <summary>Discriminator for the J1939-21 §5.10 transport-service flavor.</summary>
public enum J1939TpKind : byte
{
    /// <summary>Broadcast Announce Message (§5.10.3).</summary>
    Bam = 0,

    /// <summary>Connection Mode (RTS/CTS/EOM, §5.10.3).</summary>
    Cm = 1,
}
