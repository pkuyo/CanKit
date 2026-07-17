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

    /// <summary>Returns a copy of this options record with the provided overrides.</summary>
    public CanOpenNodeOptions With(
        TimeSpan? sdoTimeout = null,
        TimeSpan? defaultTpdoEventTimerInterval = null,
        int? eventQueueCapacity = null,
        int? maxSdoTransferBytes = null)
    {
        return new CanOpenNodeOptions
        {
            SdoTimeout = sdoTimeout ?? SdoTimeout,
            DefaultTpdoEventTimerInterval = defaultTpdoEventTimerInterval ?? DefaultTpdoEventTimerInterval,
            EventQueueCapacity = eventQueueCapacity ?? EventQueueCapacity,
            MaxSdoTransferBytes = maxSdoTransferBytes ?? MaxSdoTransferBytes,
        };
    }

    internal void Validate()
    {
        if (SdoTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SdoTimeout), SdoTimeout,
                "SDO timeout must be positive.");
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
    }
}
