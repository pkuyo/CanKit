using System;

namespace CanKit.Pro.CANopen;

/// <summary>
/// Runtime configuration for a <see cref="CanOpenNode"/>. All values are captured at construction
/// time and treated as immutable for the node's lifetime; use <see cref="With"/> to derive a
/// modified template for tests.
/// </summary>
/// <remarks>
/// The SDO client timeout defaults to one second, which matches the widely-used CANopen master
/// libraries (canopen.py, CANopenNode host tools). The heartbeat and SYNC producers are off by
/// default — enable them explicitly through the node's public API when needed.
/// </remarks>
public sealed class CanOpenNodeOptions
{
    /// <summary>Client-side SDO transfer timeout, applied to every request (initiate as well as
    /// each segment ack). CiA 301 does not specify a fixed value; one second matches common
    /// production tooling and is aggressive enough for tests on a virtual bus.</summary>
    public TimeSpan SdoTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Server-side SDO session timeout: how long an open segmented server transfer (download or
    /// upload) may idle without the peer sending the next segment / segment-ack before the
    /// session is torn down and an SDO abort (<see cref="Sdo.SdoAbortCode.SdoProtocolTimedOut"/>)
    /// is emitted. Prevents a client that starts a segmented transfer and then goes silent from
    /// pinning the server's single session slot forever (the block-transfer server side already
    /// had this guard; CiA 301 leaves the concrete value to the implementation). Deliberately
    /// longer than <see cref="SdoTimeout"/> so a well-behaved client always times out first.
    /// Defaults to 5 s.
    /// </summary>
    public TimeSpan SdoServerTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When <c>true</c> (default), a local application write to an OD entry that is mapped in an
    /// event-driven TPDO (<see cref="Pdo.TpdoTransmission.EventDriven"/>) automatically emits
    /// that TPDO — change-of-state triggering per FR-CO-006 / CiA 301 §7.3.6, without the
    /// application having to call <c>TriggerTpdoAsync</c> manually. Only application-originated
    /// writes count: bus-originated writes (SDO server download commit, RPDO unpack) run on the
    /// node's actor thread and never re-trigger TPDOs, so no bus echo loops can form. Set to
    /// <c>false</c> to restore the pure manual-trigger behavior.
    /// </summary>
    public bool EnableChangeOfStateTpdo { get; init; } = true;

    /// <summary>Interval used by the built-in TPDO event-timer scheduler for TPDOs configured
    /// with <c>TpdoTransmission.EventTimer</c>. May be overridden per-PDO at configuration time.</summary>
    public TimeSpan DefaultTpdoEventTimerInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Bounded capacity of the outbound event dispatch queue that feeds
    /// <see cref="CanOpenNode.HeartbeatReceived"/> / <see cref="CanOpenNode.HeartbeatTimeout"/> /
    /// <see cref="CanOpenNode.EmcyReceived"/> / <see cref="CanOpenNode.SyncReceived"/> /
    /// <see cref="CanOpenNode.RpdoReceived"/> / <see cref="CanOpenNode.NmtCommandReceived"/>
    /// subscribers. The node uses a bounded <see cref="System.Threading.Channels.Channel{T}"/>
    /// with drop-oldest semantics: when a subscriber cannot keep up, the queue silently
    /// discards the oldest pending events so it never grows past this bound and the actor
    /// loop is never blocked by a slow handler. Defaults to 64.
    /// <see cref="CanOpenNode.BackgroundExceptionOccurred"/> is dispatched synchronously and
    /// is not subject to this bound — it is a low-frequency diagnostic signal that must not
    /// be silently dropped by queue backpressure.
    /// </summary>
    public int EventQueueCapacity { get; init; } = 64;

    /// <summary>
    /// Upper bound (in bytes) on the total payload of a single segmented SDO transfer, applied
    /// both to server-side segmented downloads (initiator's declared 32-bit length) and to
    /// client-side segmented upload responses (server's declared 32-bit length). Bounds a
    /// hostile / buggy peer's ability to drive us into an unbounded <c>new byte[declaredLen]</c>
    /// allocation: any initiate declaring more than this many bytes is aborted with the CiA 301
    /// "out of memory" SDO abort code (0x05040005) rather than being honored. Fixed-width OD
    /// entries are additionally capped to their declared size at the type-check layer above;
    /// this limit only matters for <see cref="OdDataType.Domain"/> payloads (and for the
    /// client's upload path where the OD type is not yet known). Defaults to 1 MiB, which is
    /// well above any realistic CiA-301 profile payload but small enough to reject an accidental
    /// or malicious 4 GiB initiate outright.
    /// </summary>
    public int MaxSdoTransferBytes { get; init; } = 1 * 1024 * 1024;

    /// <summary>
    /// Auto-select threshold (bytes) at or above which the SDO client uses block transfer
    /// (FR-CO-004 / CiA 301 §7.2.4.3.15) for an <see cref="Sdo.SdoTransferMode.Auto"/> upload or
    /// download instead of the segmented protocol. Below the threshold the existing expedited /
    /// segmented codecs are used. Callers can bypass the threshold by explicitly passing
    /// <see cref="Sdo.SdoTransferMode.Block"/>. Defaults to 128 bytes.
    /// </summary>
    public int SdoBlockThresholdBytes { get; init; } = 128;

    /// <summary>
    /// Preferred block size (segments per sub-block, 1..127) that this node advertises when it
    /// acts as a block-transfer receiver (server for download, client for upload). CiA 301
    /// §7.2.4.3.15 permits the receiver to choose any value in [1,127]; peers with a smaller
    /// window will re-negotiate downward via their own initiate. Defaults to 127.
    /// </summary>
    public byte SdoBlockSize { get; init; } = 127;

    /// <summary>
    /// When <c>true</c> this node advertises CRC-16/XMODEM support on block-transfer initiates
    /// (cc / sc bit) and both computes and validates the CRC on the end-of-block frame. The peer
    /// must also set its CRC bit for the CRC to be exchanged (per CiA 301 §7.2.4.3.15 the CRC is
    /// only carried when both endpoints advertise support). Defaults to <c>true</c>.
    /// </summary>
    public bool SdoBlockCrcSupported { get; init; } = true;

    /// <summary>
    /// Maximum number of sub-block retransmissions per block transfer before the node aborts
    /// (CiA 301 §7.2.4.3.15): on a partial sub-block ACK (ackseq &lt; segments sent) the sender
    /// rewinds to the first unconfirmed segment and resends, up to this many times per transfer
    /// — a bound against peers that never confirm progress. Applies to the download client and
    /// the upload server alike. <c>0</c> restores the previous MVP behavior (abort on the first
    /// partial ACK). Defaults to 3.
    /// </summary>
    public int SdoBlockMaxRetransmissions { get; init; } = 3;

    /// <summary>
    /// When <c>true</c> this node answers a Node-Guarding RTR (COB-ID <c>0x700 + own node-id</c>)
    /// with a one-byte data frame carrying the current NMT state (bits 0..6) and an alternating
    /// toggle bit (bit 7), per CiA 301 §7.2.8.3.3. Ignored when the heartbeat producer is active
    /// (CiA 301 §7.2.8.3 makes heartbeat and node-guarding mutually exclusive on the same node).
    /// Defaults to <c>true</c> so slaves are answered out of the box.
    /// </summary>
    public bool RespondToNodeGuardingRtr { get; init; } = true;

    /// <summary>Returns a copy of this options record with the provided overrides.</summary>
    public CanOpenNodeOptions With(
        TimeSpan? sdoTimeout = null,
        TimeSpan? sdoServerTimeout = null,
        TimeSpan? defaultTpdoEventTimerInterval = null,
        int? eventQueueCapacity = null,
        int? maxSdoTransferBytes = null,
        int? sdoBlockThresholdBytes = null,
        byte? sdoBlockSize = null,
        bool? sdoBlockCrcSupported = null,
        int? sdoBlockMaxRetransmissions = null,
        bool? respondToNodeGuardingRtr = null,
        bool? enableChangeOfStateTpdo = null)
    {
        return new CanOpenNodeOptions
        {
            SdoTimeout = sdoTimeout ?? SdoTimeout,
            SdoServerTimeout = sdoServerTimeout ?? SdoServerTimeout,
            DefaultTpdoEventTimerInterval = defaultTpdoEventTimerInterval ?? DefaultTpdoEventTimerInterval,
            EventQueueCapacity = eventQueueCapacity ?? EventQueueCapacity,
            MaxSdoTransferBytes = maxSdoTransferBytes ?? MaxSdoTransferBytes,
            SdoBlockThresholdBytes = sdoBlockThresholdBytes ?? SdoBlockThresholdBytes,
            SdoBlockSize = sdoBlockSize ?? SdoBlockSize,
            SdoBlockCrcSupported = sdoBlockCrcSupported ?? SdoBlockCrcSupported,
            SdoBlockMaxRetransmissions = sdoBlockMaxRetransmissions ?? SdoBlockMaxRetransmissions,
            RespondToNodeGuardingRtr = respondToNodeGuardingRtr ?? RespondToNodeGuardingRtr,
            EnableChangeOfStateTpdo = enableChangeOfStateTpdo ?? EnableChangeOfStateTpdo,
        };
    }

    internal void Validate()
    {
        if (SdoTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SdoTimeout), SdoTimeout,
                "SDO timeout must be positive.");
        if (SdoServerTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SdoServerTimeout), SdoServerTimeout,
                "SDO server session timeout must be positive.");
        if (DefaultTpdoEventTimerInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DefaultTpdoEventTimerInterval),
                DefaultTpdoEventTimerInterval,
                "Default TPDO event-timer interval must be positive.");
        if (EventQueueCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(EventQueueCapacity), EventQueueCapacity,
                "EventQueueCapacity must be >= 1.");
        if (MaxSdoTransferBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxSdoTransferBytes), MaxSdoTransferBytes,
                "MaxSdoTransferBytes must be >= 1.");
        if (SdoBlockThresholdBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(SdoBlockThresholdBytes), SdoBlockThresholdBytes,
                "SdoBlockThresholdBytes must be >= 1.");
        if (SdoBlockSize is < 1 or > 127)
            throw new ArgumentOutOfRangeException(nameof(SdoBlockSize), SdoBlockSize,
                "SdoBlockSize must be in [1, 127] per CiA 301 §7.2.4.3.15.");
        if (SdoBlockMaxRetransmissions < 0)
            throw new ArgumentOutOfRangeException(nameof(SdoBlockMaxRetransmissions), SdoBlockMaxRetransmissions,
                "SdoBlockMaxRetransmissions must be >= 0.");
    }
}
