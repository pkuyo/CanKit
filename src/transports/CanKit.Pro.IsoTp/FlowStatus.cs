namespace CanKit.Pro.IsoTp;

/// <summary>
/// Flow-status field of an ISO 15765-2 Flow-Control (FC) frame; low nibble of the PCI byte.
/// </summary>
public enum FlowStatus : byte
{
    /// <summary>
    /// Clear-To-Send — the receiver is ready to accept up to <c>BS</c> Consecutive Frames.
    /// </summary>
    ClearToSend = 0x0,

    /// <summary>
    /// Wait — the receiver is not yet ready and asks the sender to wait; the sender must not
    /// transmit any Consecutive Frame until a subsequent FC with <see cref="ClearToSend"/> arrives.
    /// </summary>
    Wait = 0x1,

    /// <summary>
    /// Overflow — the receiver cannot buffer the announced PDU length and aborts the reception.
    /// </summary>
    Overflow = 0x2
}
