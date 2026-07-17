using System;

namespace CanKit.Pro.J1939;

/// <summary>
/// One application-layer SAE J1939 message: a parameter-group payload paired with the
/// per-frame routing fields (Priority, PGN, source and destination address) an application
/// needs to send or interpret it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Payload"/> is the plain user PGN payload (the same bytes that would appear as
/// SPN-bearing data on the bus). The node auto-routes it: payloads ≤ 8 bytes go on the wire as
/// one direct 29-bit CAN frame (SRS FR-J1939-006 single-frame path); payloads &gt; 8 bytes are
/// transported through the SAE J1939-21 §5.10 transport protocol via the shared
/// <c>CanKit.Pro.J1939Tp</c> channel — TP.BAM for a global destination, TP.CM for a specific
/// destination.
/// </para>
/// <para>
/// <see cref="DestinationAddress"/> is significant only when the PGN's PF byte is a PDU1
/// (peer-to-peer) value (&lt; 240). For PDU2 (broadcast-only) PGNs the destination is always
/// <see cref="CanKit.Pro.Addressing.J1939Pgn.GlobalAddress"/> (0xFF) regardless of what is
/// passed here.
/// </para>
/// </remarks>
public readonly struct J1939Message : IEquatable<J1939Message>
{
    /// <summary>
    /// Constructs a message. <paramref name="payload"/> is copied by the sender; callers may
    /// safely mutate or reuse the source buffer afterwards.
    /// </summary>
    /// <param name="pgn">Parameter Group Number (18-bit).</param>
    /// <param name="payload">The message payload (0..1785 bytes).</param>
    /// <param name="priority">SAE J1939-21 priority (0..7, 0 = highest); defaults to 6, the
    /// conventional non-safety priority.</param>
    /// <param name="sourceAddress">Node's own source address. For inbound messages, this is the
    /// remote sender's SA; for outbound sends, the node fills this in from its own claimed
    /// address, so any value passed here is ignored.</param>
    /// <param name="destinationAddress">Destination address for PDU1 PGNs; ignored for PDU2
    /// PGNs (always 0xFF on the wire). Defaults to
    /// <see cref="CanKit.Pro.Addressing.J1939Pgn.GlobalAddress"/> (0xFF).</param>
    public J1939Message(uint pgn, ReadOnlyMemory<byte> payload, byte priority = 6,
        byte sourceAddress = 0xFE, byte destinationAddress = 0xFF)
    {
        Pgn = pgn;
        Payload = payload;
        Priority = priority;
        SourceAddress = sourceAddress;
        DestinationAddress = destinationAddress;
    }

    /// <summary>The Parameter Group Number (18-bit value; SAE J1939-21).</summary>
    public uint Pgn { get; }

    /// <summary>User payload bytes (no framing overhead).</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>
    /// SAE J1939 priority (0..7, 0 = highest).
    /// </summary>
    /// <remarks>
    /// Only the single-frame (payload ≤ 8 bytes) send path encodes this value into the 29-bit
    /// CAN ID directly. Multi-frame (&gt; 8 bytes) sends go through the shared J1939-TP channel
    /// which uses its own <see cref="CanKit.Pro.J1939Tp.J1939TpOptions.Priority"/> (default 7)
    /// for TP.CM / TP.DT frames — a per-send priority is not part of the current
    /// <see cref="CanKit.Pro.J1939Tp.IJ1939TpChannel"/> API surface (Copilot 3600424623). To
    /// control the wire priority of multi-frame traffic, set
    /// <see cref="CanKit.Pro.J1939.J1939NodeOptions.TransportOptions"/> when opening the node.
    /// </remarks>
    public byte Priority { get; }

    /// <summary>Source address (the sender's claimed address).</summary>
    public byte SourceAddress { get; }

    /// <summary>Destination address for PDU1 PGNs, or 0xFF for PDU2/broadcast.</summary>
    public byte DestinationAddress { get; }

    /// <summary>True when the wire transport was TP (<see cref="Payload"/> length &gt; 8).</summary>
    public bool WasMultiFrame => Payload.Length > 8;

    /// <inheritdoc />
    public bool Equals(J1939Message other) =>
        Pgn == other.Pgn && Priority == other.Priority && SourceAddress == other.SourceAddress &&
        DestinationAddress == other.DestinationAddress && Payload.Span.SequenceEqual(other.Payload.Span);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is J1939Message other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        unchecked((int)((Pgn * 397) ^ (uint)(Priority << 24 | SourceAddress << 16 | DestinationAddress << 8 | Payload.Length)));
}
