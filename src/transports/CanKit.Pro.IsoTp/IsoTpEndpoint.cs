using System;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// Minimal ISO-TP endpoint value type used by <see cref="IsoTpFrameCodec"/> to decide the
/// TX/RX CAN identifiers and whether the first payload byte is reserved for an address-extension
/// byte. Runtime channel/session objects live outside this codec-only assembly.
/// </summary>
public readonly struct IsoTpEndpoint : IEquatable<IsoTpEndpoint>
{
    /// <summary>Outbound (this-node -> peer) CAN identifier.</summary>
    public uint TxCanId { get; }

    /// <summary>Inbound (peer -> this-node) CAN identifier.</summary>
    public uint RxCanId { get; }

    /// <summary>Whether <see cref="TxCanId"/>/<see cref="RxCanId"/> use the 29-bit extended
    /// CAN-ID format (true) or the 11-bit standard format (false).</summary>
    public bool IsExtendedCanId { get; }

    /// <summary>Addressing mode; decides whether an address-extension byte is present.</summary>
    public IsoTpAddressingMode AddressingMode { get; }

    /// <summary>
    /// Address-extension byte written as the first byte of every outbound frame's CAN payload.
    /// Only meaningful when <see cref="UsesAddressExtension"/> is <c>true</c>.
    /// </summary>
    /// <remarks>
    /// For <see cref="IsoTpAddressingMode.Extended"/> this carries the <em>target</em> address
    /// (N_TA) — the peer's address — because ISO 15765-2 §5.3.2.4 puts N_TA in the first payload
    /// byte of outbound frames. For <see cref="IsoTpAddressingMode.Mixed"/> the TX and RX
    /// extension bytes are the same value. Inbound frames are filtered against
    /// <see cref="RxAddressExtension"/>, which for Extended addressing is a different value
    /// (the source address / N_SA that the peer put in *its* outbound frames).
    /// </remarks>
    public byte AddressExtension { get; }

    /// <summary>
    /// Address-extension byte expected as the first byte of every inbound frame's CAN payload.
    /// Only meaningful when <see cref="UsesAddressExtension"/> is <c>true</c>.
    /// </summary>
    /// <remarks>
    /// For <see cref="IsoTpAddressingMode.Extended"/> this is the <em>source</em> address
    /// (N_SA / this node's own address) because the peer places its target address — which is
    /// us — in its outbound N_TA byte. For <see cref="IsoTpAddressingMode.Mixed"/> this is the
    /// same value as <see cref="AddressExtension"/>. For normal / normal-fixed modes this
    /// property is unused (<see cref="UsesAddressExtension"/> is <c>false</c>).
    /// </remarks>
    public byte RxAddressExtension { get; }

    /// <summary>
    /// <c>true</c> when <see cref="AddressingMode"/> is <see cref="IsoTpAddressingMode.Extended"/>
    /// or <see cref="IsoTpAddressingMode.Mixed"/>; the first payload byte is then consumed by the
    /// address-extension byte and only the remaining bytes carry PCI + data.
    /// </summary>
    public bool UsesAddressExtension =>
        AddressingMode is IsoTpAddressingMode.Extended or IsoTpAddressingMode.Mixed;

    /// <summary>
    /// The number of payload bytes reserved for the address-extension byte (0 or 1).
    /// </summary>
    public int AddressExtensionSize => UsesAddressExtension ? 1 : 0;

    private IsoTpEndpoint(uint txCanId, uint rxCanId, bool isExtendedCanId,
        IsoTpAddressingMode addressingMode, byte addressExtension, byte rxAddressExtension)
    {
        TxCanId = txCanId;
        RxCanId = rxCanId;
        IsExtendedCanId = isExtendedCanId;
        AddressingMode = addressingMode;
        AddressExtension = addressExtension;
        RxAddressExtension = rxAddressExtension;
    }

    /// <summary>Creates an endpoint using ISO 15765-2 <em>Normal</em> addressing.</summary>
    public static IsoTpEndpoint Normal(uint txCanId, uint rxCanId, bool isExtendedCanId = false)
        => new(txCanId, rxCanId, isExtendedCanId, IsoTpAddressingMode.Normal, 0, 0);

    /// <summary>Creates an endpoint using ISO 15765-2 <em>Normal-Fixed</em> addressing (29-bit).</summary>
    public static IsoTpEndpoint NormalFixed(uint txCanId, uint rxCanId)
        => new(txCanId, rxCanId, isExtendedCanId: true, IsoTpAddressingMode.NormalFixed, 0, 0);

    /// <summary>
    /// Creates an endpoint using ISO 15765-2 <em>Extended</em> addressing.
    /// <paramref name="targetAddress"/> is written as the first byte of every outbound frame
    /// (the peer's address, N_TA); <paramref name="sourceAddress"/> is the value expected as the
    /// first byte of every inbound frame (this node's own address, N_SA — which appears in the
    /// peer's outbound N_TA byte). Passing the two backwards causes the RX filter to drop every
    /// inbound frame.
    /// </summary>
    public static IsoTpEndpoint Extended(uint txCanId, uint rxCanId, byte sourceAddress,
        byte targetAddress, bool isExtendedCanId = false)
        => new(txCanId, rxCanId, isExtendedCanId, IsoTpAddressingMode.Extended,
            addressExtension: targetAddress, rxAddressExtension: sourceAddress);

    /// <summary>
    /// Creates an endpoint using ISO 15765-2 <em>Mixed</em> addressing. The
    /// <paramref name="addressExtension"/> byte is written as the first byte of every outbound
    /// frame and is expected as the first byte of every inbound frame (same value both ways).
    /// </summary>
    public static IsoTpEndpoint Mixed(uint txCanId, uint rxCanId, byte addressExtension,
        bool isExtendedCanId = false)
        => new(txCanId, rxCanId, isExtendedCanId, IsoTpAddressingMode.Mixed,
            addressExtension: addressExtension, rxAddressExtension: addressExtension);

    /// <inheritdoc/>
    public bool Equals(IsoTpEndpoint other) =>
        TxCanId == other.TxCanId &&
        RxCanId == other.RxCanId &&
        IsExtendedCanId == other.IsExtendedCanId &&
        AddressingMode == other.AddressingMode &&
        AddressExtension == other.AddressExtension &&
        RxAddressExtension == other.RxAddressExtension;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is IsoTpEndpoint other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) ^ (int)TxCanId;
            hash = (hash * 31) ^ (int)RxCanId;
            hash = (hash * 31) ^ IsExtendedCanId.GetHashCode();
            hash = (hash * 31) ^ (int)AddressingMode;
            hash = (hash * 31) ^ AddressExtension;
            hash = (hash * 31) ^ RxAddressExtension;
            return hash;
        }
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(IsoTpEndpoint left, IsoTpEndpoint right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(IsoTpEndpoint left, IsoTpEndpoint right) => !left.Equals(right);
}
