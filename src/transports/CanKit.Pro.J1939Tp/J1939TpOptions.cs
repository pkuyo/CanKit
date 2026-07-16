using System;

namespace CanKit.Pro.J1939Tp;

/// <summary>
/// Configuration for a <see cref="IJ1939TpChannel"/>: J1939-21 §5.10 timing parameters, transmit
/// priority, and per-channel bounds. All values are captured at channel construction time and
/// treated as immutable for its lifetime.
/// </summary>
/// <remarks>
/// The J1939-21 §5.10.2.4 recommended values are hard-coded defaults; production callers can
/// override any subset via <see cref="With"/>. Every timer defaults to its standard value:
/// <list type="bullet">
///   <item><description><see cref="T1"/> = 750 ms — TP.DT gap timeout at the receiver.</description></item>
///   <item><description><see cref="T2"/> = 1250 ms — TP.CM CTS gap timeout at the sender.</description></item>
///   <item><description><see cref="T3"/> = 1250 ms — TP.CM EOM/ack gap timeout at the sender.</description></item>
///   <item><description><see cref="T4"/> = 1050 ms — TP.CM "still-alive" hold timeout at the receiver.</description></item>
///   <item><description><see cref="Tr"/> = 200 ms — receiver-side response deadline after emitting a CTS.</description></item>
///   <item><description><see cref="Th"/> = 500 ms — hold-off between two consecutive BAM DTs.</description></item>
/// </list>
/// </remarks>
public sealed class J1939TpOptions
{
    /// <summary>T1 — TP.DT gap timeout at the receiver (J1939-21 §5.10.2.4). Default 750 ms.</summary>
    public TimeSpan T1 { get; init; } = TimeSpan.FromMilliseconds(750);

    /// <summary>T2 — CTS gap timeout at the sender (§5.10.2.4). Default 1250 ms.</summary>
    public TimeSpan T2 { get; init; } = TimeSpan.FromMilliseconds(1250);

    /// <summary>T3 — EndOfMsgAck gap timeout at the sender (§5.10.2.4). Default 1250 ms.</summary>
    public TimeSpan T3 { get; init; } = TimeSpan.FromMilliseconds(1250);

    /// <summary>T4 — receiver-side "still-alive" hold timeout while waiting for the next TP.CM (§5.10.2.4). Default 1050 ms.</summary>
    public TimeSpan T4 { get; init; } = TimeSpan.FromMilliseconds(1050);

    /// <summary>Tr — receiver response deadline after emitting a CTS (§5.10.2.4). Default 200 ms.</summary>
    public TimeSpan Tr { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Th — minimum hold-off between two consecutive BAM TP.DT frames on the wire (§5.10.3
    /// "50..200 ms"). Default 50 ms to stay at the lower recommended bound while still gating
    /// against a receiver that cannot keep up.
    /// </summary>
    public TimeSpan Th { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// TX priority for TP.CM / TP.DT frames sent by this channel (0..7, 0 = highest). J1939-21
    /// uses 7 by default for its transport traffic.
    /// </summary>
    public byte Priority { get; init; } = 7;

    /// <summary>
    /// Maximum number of TP.DT packets per CTS this channel advertises when it is the receiver
    /// of a TP.CM session. Must be &gt; 0. Defaults to 16, well below the 255 hard cap so a slow
    /// consumer can keep the block short.
    /// </summary>
    public byte MaxPacketsPerCts { get; init; } = 16;

    /// <summary>
    /// Bounded capacity of the internal receive buffer that holds fully reassembled PDUs waiting
    /// for the consumer. Drops the oldest PDU when full so the RX pipeline never stalls the actor.
    /// Defaults to 32.
    /// </summary>
    public int ReceiveBufferCapacity { get; init; } = 32;

    /// <summary>
    /// Convenience clone that returns a new instance with the provided overrides. Useful for
    /// tests that want to tweak one field of a shared default template.
    /// </summary>
    public J1939TpOptions With(
        TimeSpan? t1 = null,
        TimeSpan? t2 = null,
        TimeSpan? t3 = null,
        TimeSpan? t4 = null,
        TimeSpan? tr = null,
        TimeSpan? th = null,
        byte? priority = null,
        byte? maxPacketsPerCts = null,
        int? receiveBufferCapacity = null)
        => new()
        {
            T1 = t1 ?? T1,
            T2 = t2 ?? T2,
            T3 = t3 ?? T3,
            T4 = t4 ?? T4,
            Tr = tr ?? Tr,
            Th = th ?? Th,
            Priority = priority ?? Priority,
            MaxPacketsPerCts = maxPacketsPerCts ?? MaxPacketsPerCts,
            ReceiveBufferCapacity = receiveBufferCapacity ?? ReceiveBufferCapacity,
        };
}
