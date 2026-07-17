using System;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Pro.Addressing;

namespace CanKit.Pro.J1939;

/// <summary>
/// One application-layer SAE J1939 node bound to a single physical bus and a single 64-bit
/// <see cref="J1939Name"/> identity. The node owns the SAE J1939-81 address-claim state
/// (SRS FR-J1939-003/004), routes outbound PGNs through direct 29-bit frames (≤ 8 bytes) or
/// the shared J1939-TP channel (&gt; 8 bytes, SRS FR-J1939-006), and surfaces every inbound
/// application PGN (including Request-PGN, SRS FR-J1939-005) via
/// <see cref="MessageReceived"/>.
/// </summary>
/// <remarks>
/// <para>
/// The node composes on the CanKit.Pro L2 services (<see cref="CanKit.Pro.RawCan.ICanBusService"/>
/// for RX demux and TX confirmation, <see cref="CanKit.Pro.Actor.IProtocolActor"/> for
/// single-writer state, <see cref="CanKit.Pro.Reliability.DeadlineScheduler"/> for the
/// SAE J1939-81 §4.4.3.3 250 ms arbitration window and periodic-send timing) and never
/// touches vendor-specific SDKs directly.
/// </para>
/// <para>
/// <see cref="IDisposable.Dispose"/> is thread-safe and idempotent. Disposal cancels any
/// in-flight <see cref="ClaimAddressAsync"/> or <see cref="SendAsync"/> call and unwinds the
/// underlying subscriptions and transport channel.
/// </para>
/// </remarks>
public interface IJ1939Node : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// The node's claimed source address, or <c>null</c> when it has none
    /// (<see cref="J1939ClaimState.NotClaimed"/> / <see cref="J1939ClaimState.CannotClaim"/>).
    /// </summary>
    byte? Address { get; }

    /// <summary>The node's immutable 64-bit SAE J1939-81 NAME (from
    /// <see cref="J1939NodeOptions.Name"/>).</summary>
    J1939Name Name { get; }

    /// <summary>Current SAE J1939-81 claim state (SRS FR-J1939-003/004).</summary>
    J1939ClaimState ClaimState { get; }

    /// <summary>Node options captured at construction.</summary>
    J1939NodeOptions Options { get; }

    /// <summary>
    /// Raised on the actor's loop thread for every inbound application PGN — both direct
    /// single-frame PGNs and reassembled J1939-TP payloads (SRS FR-J1939-001/006). Request
    /// PGN (0xEA00) messages are also surfaced here so applications can respond in kind
    /// (SRS FR-J1939-005). Handlers must not throw; a throwing handler is caught and reported
    /// via <see cref="BackgroundExceptionOccurred"/>.
    /// </summary>
    event EventHandler<J1939Message>? MessageReceived;

    /// <summary>
    /// Raised whenever the node's <see cref="ClaimState"/> transitions (a claim starts,
    /// succeeds, is lost, or Cannot Claim is broadcast).
    /// </summary>
    event EventHandler<J1939ClaimEventArgs>? AddressClaimChanged;

    /// <summary>
    /// Raised when a background failure (subscription faulted, actor exception, event-handler
    /// throw, TP channel background error) must be surfaced to the application.
    /// </summary>
    event EventHandler<Exception>? BackgroundExceptionOccurred;

    /// <summary>
    /// Runs the SAE J1939-81 address-claim procedure for <paramref name="preferredAddress"/>
    /// (SRS FR-J1939-003):
    /// <list type="number">
    ///   <item><description>Transmit Address Claim (PGN 0xEE00, SA = preferred) carrying the
    ///     node's NAME.</description></item>
    ///   <item><description>Listen for contending claims within
    ///     <see cref="J1939NodeOptions.ClaimAnnounceTimeout"/> (default 250 ms).</description></item>
    ///   <item><description>On a losing contest (peer's NAME numerically lower), transition
    ///     to <see cref="J1939ClaimState.CannotClaim"/>, broadcast Cannot Claim (SA = 0xFE)
    ///     per SAE J1939-81 §4.4.3.4 and throw
    ///     <see cref="J1939CannotClaimException"/> (SRS FR-J1939-004).</description></item>
    /// </list>
    /// </summary>
    /// <exception cref="J1939CannotClaimException">The preferred address was lost to a
    /// higher-priority NAME and no fallback was available.</exception>
    Task ClaimAddressAsync(byte preferredAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <paramref name="message"/> on the bus (SRS FR-J1939-001/006). Payloads ≤ 8 bytes
    /// go as one direct 29-bit CAN frame; larger payloads are broken into a TP.BAM (global
    /// destination) or TP.CM (specific destination) session on the shared J1939-TP channel.
    /// The task completes when the last frame of the message has been TX-confirmed (single
    /// frame) or when the TP session finishes (multi-frame).
    /// </summary>
    /// <exception cref="J1939NoAddressException">The node has not yet claimed an address.</exception>
    Task SendAsync(J1939Message message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a Request-PGN (SAE J1939-21 PGN 0xEA00, SRS FR-J1939-005). The 3-byte payload
    /// carries <paramref name="requestedPgn"/> little-endian; the request itself is a direct
    /// single-frame PGN. A destination of 0xFF makes the request global (every node responds
    /// with its usual PGN); any other value targets a specific ECU.
    /// </summary>
    Task RequestPgnAsync(uint requestedPgn, byte destinationAddress = 0xFF,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts sending <paramref name="message"/> periodically at the given <paramref name="period"/>
    /// (SRS FR-J1939-007). Single-frame PGNs (≤ 8 byte payload) are dispatched through the L1
    /// <c>ICanBus.TransmitPeriodic</c> / <c>IPeriodicTx</c> handle (bus-native cyclic TX where the
    /// adapter supports it, software fallback otherwise), so timing does not compete with the
    /// node's actor loop; multi-frame PGNs (&gt; 8 byte) keep a software loop that opens a fresh
    /// J1939-TP session per emission. The schedule tracks the node's SAE J1939-81 claim state:
    /// on a fresh claim with a different SA the emitted 29-bit ID is updated in-place via
    /// <c>IPeriodicTx.Update</c>; on address loss (leaving <see cref="J1939ClaimState.Claimed"/>)
    /// the periodic handle is stopped, and it is re-armed on the next successful claim.
    /// Send failures are surfaced via <see cref="BackgroundExceptionOccurred"/>. Disposing the
    /// returned handle stops the schedule. Multiple concurrent schedules for the same PGN are
    /// allowed (callers may want to send the same PGN to two destinations).
    /// The caller supplies <paramref name="period"/>; mapping application PGNs to their
    /// SAE J1939-71 standard rate is the caller's responsibility.
    /// <para>
    /// <strong>Payload snapshot:</strong> <paramref name="message"/>'s payload is snapshotted
    /// into an owned buffer when this method returns, and every emission (single-frame or
    /// multi-frame, L1 or software fallback) transmits that snapshot. In-place mutation of
    /// the caller's original buffer after <c>StartPeriodicSend</c> is NOT observed on the
    /// wire — this matches <see cref="J1939Message"/>'s "payload is copied by the sender"
    /// contract and keeps the L1 <c>IPeriodicTx</c> path and the software-fallback path
    /// behaviourally identical (Bugbot 3604566680). To change the transmitted data, dispose
    /// the returned handle and start a fresh schedule with a new <see cref="J1939Message"/>.
    /// </para>
    /// </summary>
    /// <exception cref="J1939NoAddressException">The node has not yet claimed an address for
    /// the single-frame path (matches <see cref="SendAsync"/>'s pre-flight gate).</exception>
    IDisposable StartPeriodicSend(J1939Message message, TimeSpan period);
}
