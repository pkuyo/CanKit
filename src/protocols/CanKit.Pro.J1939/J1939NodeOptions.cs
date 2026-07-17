using System;
using CanKit.Pro.Addressing;
using CanKit.Pro.J1939Tp;

namespace CanKit.Pro.J1939;

/// <summary>
/// Immutable configuration for <see cref="IJ1939Node"/>. Captures the node's identity
/// (<see cref="Name"/>), the SAE J1939-81 arbitration window <see cref="ClaimAnnounceTimeout"/>,
/// receive-buffer bounds, and the transport-protocol options passed through to the shared
/// <see cref="IJ1939TpChannel"/> for &gt;8-byte payloads.
/// </summary>
public sealed class J1939NodeOptions
{
    /// <summary>Constructs options with the given 64-bit NAME. All other fields default.</summary>
    /// <param name="name">The node's SAE J1939-81 NAME used for address-claim arbitration.</param>
    public J1939NodeOptions(J1939Name name)
    {
        Name = name;
    }

    /// <summary>The node's SAE J1939-81 NAME (used for address claim arbitration).</summary>
    public J1939Name Name { get; }

    /// <summary>
    /// Duration <see cref="IJ1939Node.ClaimAddressAsync"/> waits after transmitting the initial
    /// Address Claim (PGN 0xEE00) before declaring the address claimed. SAE J1939-81 §4.4.3.3
    /// mandates 250 ms — that is the default. Shorten for tests only.
    /// </summary>
    public TimeSpan ClaimAnnounceTimeout { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Default priority reported for inbound reassembled J1939-TP payloads, where the transport
    /// channel does not currently expose the original TP.CM / TP.DT CAN priority. Defaults to 6.
    /// </summary>
    public byte DefaultPriority { get; init; } = 6;

    /// <summary>
    /// Priority used by this node for Address Claim / Cannot Claim (PGN 0xEE00) and Request-PGN
    /// (PGN 0xEA00) frames. Defaults to 6.
    /// </summary>
    public byte ClaimPriority { get; init; } = 6;

    /// <summary>
    /// Bounded capacity of the internal receive buffer. Drops the oldest message when full so
    /// a slow consumer never stalls the node's actor loop. Defaults to 128.
    /// </summary>
    public int ReceiveBufferCapacity { get; init; } = 128;

    /// <summary>
    /// Options forwarded to the shared <see cref="IJ1939TpChannel"/> for multi-frame payloads
    /// (SRS FR-J1939-006). Defaults to a fresh <see cref="J1939TpOptions"/>. Ignored when the
    /// caller supplies their own pre-built channel to the factory.
    /// </summary>
    public J1939TpOptions TransportOptions { get; init; } = new J1939TpOptions();

    /// <summary>Validates invariants. Called from <see cref="J1939Node.Open(CanKit.Abstractions.API.Can.ICanBus, J1939NodeOptions)"/>.</summary>
    internal void Validate()
    {
        if (ClaimAnnounceTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ClaimAnnounceTimeout), ClaimAnnounceTimeout,
                "ClaimAnnounceTimeout must be positive.");
        if (DefaultPriority > 7)
            throw new ArgumentOutOfRangeException(nameof(DefaultPriority), DefaultPriority,
                "J1939 priority must be in [0, 7].");
        if (ClaimPriority > 7)
            throw new ArgumentOutOfRangeException(nameof(ClaimPriority), ClaimPriority,
                "J1939 priority must be in [0, 7].");
        if (ReceiveBufferCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(ReceiveBufferCapacity), ReceiveBufferCapacity,
                "ReceiveBufferCapacity must be >= 1.");
        if (TransportOptions is null)
            throw new ArgumentNullException(nameof(TransportOptions));
    }
}
