namespace CanKit.Pro.J1939Tp;

/// <summary>
/// Connection Abort reason codes carried in byte 1 of the TP.CM Abort control frame
/// (SAE J1939-21 §5.10.5 / A-2). The list mirrors the standard's own enumerated values so we
/// can round-trip them on the wire without translation; callers should treat unknown values as
/// <see cref="Unknown"/>.
/// </summary>
public enum J1939TpAbortReason : byte
{
    /// <summary>Unknown or undefined reason (default).</summary>
    Unknown = 0,

    /// <summary>
    /// One or more of the previously allocated resources for this connection are needed by a
    /// higher priority process (§5.10.5, code 1).
    /// </summary>
    ResourceNeededForHigherPriorityProcess = 1,

    /// <summary>
    /// System resources were needed for another task so this connection managed session was
    /// terminated (§5.10.5, code 2).
    /// </summary>
    NoResourcesAvailable = 2,

    /// <summary>
    /// A timeout occurred and the session is terminated. Used by every T1/T2/T3/T4/Tr/Th
    /// enforcement path in this stack (§5.10.5, code 3).
    /// </summary>
    Timeout = 3,

    /// <summary>Unexpected CTS numPackets value (larger than remaining).</summary>
    UnexpectedCtsNumPackets = 4,

    /// <summary>Unexpected CTS next-packet-SN.</summary>
    UnexpectedCtsSequenceNumber = 5,

    /// <summary>Retransmit requests are not supported by this implementation.</summary>
    RetransmitNotSupported = 6,

    /// <summary>Session already open with this peer for this PGN.</summary>
    SessionAlreadyOpen = 7,

    /// <summary>The receiver aborted the session for an unspecified reason.</summary>
    ReceiverAbort = 250,
}
