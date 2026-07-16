namespace CanKit.Pro.IsoTp;

/// <summary>
/// Protocol-Control-Information (PCI) type nibble used in the first byte of every ISO 15765-2
/// (ISO-TP) frame payload. The value is the high nibble of the PCI byte.
/// </summary>
public enum PciType : byte
{
    /// <summary>Single Frame (SF) — the entire PDU fits in one CAN frame.</summary>
    SingleFrame = 0x0,

    /// <summary>First Frame (FF) — first segment of a segmented, multi-frame PDU.</summary>
    FirstFrame = 0x1,

    /// <summary>Consecutive Frame (CF) — subsequent segment of a segmented PDU.</summary>
    ConsecutiveFrame = 0x2,

    /// <summary>Flow Control (FC) — receiver-to-sender BS/STmin/FS handshake.</summary>
    FlowControl = 0x3
}
