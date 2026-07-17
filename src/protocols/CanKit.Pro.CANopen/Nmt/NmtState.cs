namespace CanKit.Pro.CANopen.Nmt;

/// <summary>
/// CANopen NMT slave state (CiA 301 §7.3.2). The bootup message carries the byte
/// <c>0x00</c> which is treated as an unsolicited transition into
/// <see cref="PreOperational"/> in this MVP.
/// </summary>
public enum NmtState : byte
{
    /// <summary>Node has just powered up or been reset and has not yet sent a heartbeat.</summary>
    Initializing = 0x00,

    /// <summary>Node reports itself as <c>Stopped</c> in a heartbeat (0x04).</summary>
    Stopped = 0x04,

    /// <summary>Node reports itself as <c>Operational</c> in a heartbeat (0x05).</summary>
    Operational = 0x05,

    /// <summary>Node reports itself as <c>Pre-Operational</c> in a heartbeat (0x7F).</summary>
    PreOperational = 0x7F,
}

/// <summary>
/// CiA 301 §7.2.8 NMT master command specifiers. Encoded in byte 0 of the NMT master frame at
/// COB-ID <c>0x000</c>, followed by the target node-id in byte 1 (0 = broadcast to all nodes).
/// </summary>
public enum NmtCommand : byte
{
    /// <summary>Start Remote Node — transitions the target into <see cref="NmtState.Operational"/>.</summary>
    Start = 0x01,

    /// <summary>Stop Remote Node — transitions the target into <see cref="NmtState.Stopped"/>.</summary>
    Stop = 0x02,

    /// <summary>Enter Pre-Operational — transitions the target into
    /// <see cref="NmtState.PreOperational"/>.</summary>
    EnterPreOperational = 0x80,

    /// <summary>Reset Node — full application reset (implies reset communication).</summary>
    ResetNode = 0x81,

    /// <summary>Reset Communication — reset only the communication profile / heartbeats.</summary>
    ResetCommunication = 0x82,
}
