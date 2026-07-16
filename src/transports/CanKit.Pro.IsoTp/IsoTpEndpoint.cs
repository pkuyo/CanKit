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
    /// Address-extension byte written as the first byte of every outbound frame's CAN payload,
    /// and expected as the first byte of every inbound frame's CAN payload. Only meaningful when
    /// <see cref="UsesAddressExtension"/> is <c>true</c>.
    /// </summary>
    public byte AddressExtension { get; }

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
        IsoTpAddressingMode addressingMode, byte addressExtension)
    {
        TxCanId = txCanId;
        RxCanId = rxCanId;
        IsExtendedCanId = isExtendedCanId;
        AddressingMode = addressingMode;
        AddressExtension = addressExtension;
    }

    /// <summary>Creates an endpoint using ISO 15765-2 <em>Normal</em> addressing.</summary>
    public static IsoTpEndpoint Normal(uint txCanId, uint rxCanId, bool isExtendedCanId = false)
        => new(txCanId, rxCanId, isExtendedCanId, IsoTpAddressingMode.Normal, 0);

    /// <summary>Creates an endpoint using ISO 15765-2 <em>Normal-Fixed</em> addressing (29-bit).</summary>
    public static IsoTpEndpoint NormalFixed(uint txCanId, uint rxCanId)
        => new(txCanId, rxCanId, isExtendedCanId: true, IsoTpAddressingMode.NormalFixed, 0);

    /// <summary>
    /// Creates an endpoint using ISO 15765-2 <em>Extended</em> addressing. The <paramref name="targetAddress"/>
    /// is placed as the first byte of every outbound frame; the first byte of every inbound frame
    /// is expected to be a matching source address (compared against <paramref name="sourceAddress"/>
    /// by the runtime, not by the codec itself).
    /// </summary>
    public static IsoTpEndpoint Extended(uint txCanId, uint rxCanId, byte sourceAddress,
        byte targetAddress, bool isExtendedCanId = false)
        => new(txCanId, rxCanId, isExtendedCanId, IsoTpAddressingMode.Extended, targetAddress);

    /// <summary>
    /// Creates an endpoint using ISO 15765-2 <em>Mixed</em> addressing. The
    /// <paramref name="addressExtension"/> byte is written as the first byte of every outbound
    /// frame and is expected as the first byte of every inbound frame.
    /// </summary>
    public static IsoTpEndpoint Mixed(uint txCanId, uint rxCanId, byte addressExtension,
        bool isExtendedCanId = false)
        => new(txCanId, rxCanId, isExtendedCanId, IsoTpAddressingMode.Mixed, addressExtension);

    /// <inheritdoc/>
    public bool Equals(IsoTpEndpoint other) =>
        TxCanId == other.TxCanId &&
        RxCanId == other.RxCanId &&
        IsExtendedCanId == other.IsExtendedCanId &&
        AddressingMode == other.AddressingMode &&
        AddressExtension == other.AddressExtension;

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
            return hash;
        }
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(IsoTpEndpoint left, IsoTpEndpoint right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(IsoTpEndpoint left, IsoTpEndpoint right) => !left.Equals(right);
}
