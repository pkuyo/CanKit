namespace CanKit.Pro.IsoTp;

/// <summary>
/// ISO 15765-2 addressing modes that the codec understands. The mode decides whether the first
/// byte of the CAN payload is consumed by an address-extension byte or is available for PCI/data.
/// </summary>
public enum IsoTpAddressingMode
{
    /// <summary>
    /// Normal addressing — the CAN identifier alone carries the source/target information; the
    /// whole CAN payload is available for the ISO-TP PCI and data bytes.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Normal-fixed addressing — like <see cref="Normal"/> but the 29-bit CAN identifier itself
    /// encodes the addresses (as defined by ISO 15765-2 for diagnostic communication). No
    /// address-extension byte is present in the payload.
    /// </summary>
    NormalFixed = 1,

    /// <summary>
    /// Extended addressing — the first byte of the CAN payload carries the target address on TX
    /// and the source address on RX; only the remaining bytes are available for PCI and data.
    /// </summary>
    Extended = 2,

    /// <summary>
    /// Mixed addressing — the first byte of the CAN payload carries an address extension byte
    /// shared by TX and RX; only the remaining bytes are available for PCI and data.
    /// </summary>
    Mixed = 3
}
