using System;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// Parsed ISO 15765-2 Protocol-Control-Information (PCI) view produced by
/// <see cref="IsoTpFrameCodec.TryParsePci"/>. Only the fields relevant to the parsed
/// <see cref="PciType"/> are populated; all other fields are default-valued.
/// </summary>
public readonly struct Pci : IEquatable<Pci>
{
    /// <summary>PCI type (nibble of the first PCI byte).</summary>
    public PciType Type { get; }

    /// <summary>
    /// Length field. For <see cref="PciType.SingleFrame"/> this is the number of user data bytes
    /// (1..7 for classic-CAN, 1..62 for CAN-FD escape form). For <see cref="PciType.FirstFrame"/>
    /// this is the total PDU length (up to 4095 for the classic form and up to
    /// <see cref="IsoTpFrameCodec.MaxFdFirstFrameLength"/> for the CAN-FD escape form). Otherwise
    /// zero.
    /// </summary>
    public int Length { get; }

    /// <summary>Sequence number (0..15) for <see cref="PciType.ConsecutiveFrame"/>; otherwise 0.</summary>
    public byte SequenceNumber { get; }

    /// <summary>Flow status for <see cref="PciType.FlowControl"/>; otherwise <see cref="FlowStatus.ClearToSend"/>.</summary>
    public FlowStatus FlowStatus { get; }

    /// <summary>Block size (BS) for <see cref="PciType.FlowControl"/>; otherwise 0.</summary>
    public byte BlockSize { get; }

    /// <summary>Raw STmin byte for <see cref="PciType.FlowControl"/>; otherwise 0. Use
    /// <see cref="IsoTpFrameCodec.DecodeStMin(byte)"/> to convert to <see cref="TimeSpan"/>.</summary>
    public byte StMinRaw { get; }

    /// <summary>
    /// STmin converted to a <see cref="TimeSpan"/> via <see cref="IsoTpFrameCodec.DecodeStMin(byte)"/>.
    /// Only meaningful for <see cref="PciType.FlowControl"/>; <see cref="TimeSpan.Zero"/> otherwise.
    /// </summary>
    public TimeSpan StMin { get; }

    /// <summary>
    /// Byte offset (within the CAN payload span passed to
    /// <see cref="IsoTpFrameCodec.TryParsePci"/>) at which user data starts. For SF/FF this is the
    /// first data byte; for CF this is the first data byte; for FC there are no user data bytes
    /// and the value points one past the FC PCI.
    /// </summary>
    public int DataOffset { get; }

    internal Pci(PciType type, int length, byte sequenceNumber, FlowStatus flowStatus,
        byte blockSize, byte stMinRaw, TimeSpan stMin, int dataOffset)
    {
        Type = type;
        Length = length;
        SequenceNumber = sequenceNumber;
        FlowStatus = flowStatus;
        BlockSize = blockSize;
        StMinRaw = stMinRaw;
        StMin = stMin;
        DataOffset = dataOffset;
    }

    /// <inheritdoc/>
    public bool Equals(Pci other) =>
        Type == other.Type &&
        Length == other.Length &&
        SequenceNumber == other.SequenceNumber &&
        FlowStatus == other.FlowStatus &&
        BlockSize == other.BlockSize &&
        StMinRaw == other.StMinRaw &&
        DataOffset == other.DataOffset;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Pci other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) ^ (int)Type;
            hash = (hash * 31) ^ Length;
            hash = (hash * 31) ^ SequenceNumber;
            hash = (hash * 31) ^ (int)FlowStatus;
            hash = (hash * 31) ^ BlockSize;
            hash = (hash * 31) ^ StMinRaw;
            hash = (hash * 31) ^ DataOffset;
            return hash;
        }
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Pci left, Pci right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Pci left, Pci right) => !left.Equals(right);
}
