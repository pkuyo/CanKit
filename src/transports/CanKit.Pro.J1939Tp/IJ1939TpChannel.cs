using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CanKit.Pro.J1939Tp;

/// <summary>
/// One J1939-TP channel bound to a single J1939 source address on one physical bus. It carries
/// both TP.BAM (broadcast) and TP.CM (connection-mode) sessions in parallel, each in its own
/// actor-owned state (SRS FR-TP-034/035) so multiple concurrent peers do not interfere.
/// </summary>
/// <remarks>
/// <para>
/// Threading model (SRS FR-TP-034 = FR-TP-016/017 applied to J1939-TP): every session's state
/// (sequence numbers, remaining bytes, block counters, T1..T4/Tr/Th deadlines) lives inside a
/// single <see cref="Actor.IProtocolActor"/> mailbox and is only ever read/written on the
/// actor's loop thread. Callers may invoke <see cref="SendBamAsync"/> or
/// <see cref="SendCmAsync"/> concurrently from any thread; the channel serializes them per peer
/// so one PDU at a time is on the wire per session identity (source, destination, PGN).
/// </para>
/// <para>
/// Received PDUs are delivered both as an event (<see cref="DatagramReceived"/>) and via
/// <see cref="ReceiveAsync"/> / <see cref="ReceiveAllAsync"/>. Both surfaces share the same
/// bounded buffer; if it fills, the oldest datagram is dropped so the RX pipeline never stalls
/// the actor.
/// </para>
/// <para>
/// <see cref="IDisposable.Dispose"/> is thread-safe and idempotent (FR-RAW-021).
/// </para>
/// </remarks>
public interface IJ1939TpChannel : IDisposable
{
    /// <summary>The J1939 source address this channel identifies itself as on the bus.</summary>
    byte SourceAddress { get; }

    /// <summary>Options captured at construction time (immutable).</summary>
    J1939TpOptions Options { get; }

    /// <summary>
    /// Broadcasts <paramref name="payload"/> as one TP.BAM session (FR-TP-030). The task
    /// completes when the last TP.DT frame is TX-confirmed by the driver. Payload length must
    /// be in [9, 1785] bytes (J1939-21 §5.10.1: TP is only used above 8 bytes).
    /// </summary>
    /// <param name="pgn">Data PGN of the multi-packet message being announced.</param>
    /// <param name="payload">User payload; 9..1785 bytes.</param>
    /// <param name="cancellationToken">Standard .NET cancellation of the returned task.</param>
    Task SendBamAsync(uint pgn, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <paramref name="payload"/> as one TP.CM session to
    /// <paramref name="destinationAddress"/> (FR-TP-031). The task completes when the peer's
    /// EndOfMsgAck arrives (or faults with <see cref="J1939TpAbortException"/> on abort/timeout).
    /// </summary>
    /// <param name="pgn">Data PGN of the multi-packet message being sent.</param>
    /// <param name="destinationAddress">Target node's source address (must not be 0xFF).</param>
    /// <param name="payload">User payload; 9..1785 bytes.</param>
    /// <param name="cancellationToken">Standard .NET cancellation of the returned task.</param>
    Task SendCmAsync(uint pgn, byte destinationAddress, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Awaits the next fully reassembled inbound TP.BAM or TP.CM datagram, or faults with
    /// <see cref="J1939TpAbortException"/> when an in-flight reassembly is aborted (bad TP.DT
    /// sequence number, T1/Tr timeout, or peer Connection Abort). One waiter consumes the fault;
    /// subsequent receives remain available for later successful datagrams.
    /// </summary>
    Task<J1939TpDatagram> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates every reassembled inbound datagram until the channel is disposed. Reassembly
    /// aborts surface as <see cref="J1939TpAbortException"/> on the enumerating waiter (same
    /// semantics as <see cref="ReceiveAsync"/>).
    /// </summary>
    IAsyncEnumerable<J1939TpDatagram> ReceiveAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised on the actor's loop thread every time a full PDU is reassembled. The same datagram
    /// is also enqueued for <see cref="ReceiveAsync"/> / <see cref="ReceiveAllAsync"/>. Handlers
    /// must be lightweight and non-throwing; a throwing handler is caught and surfaced via
    /// <see cref="BackgroundExceptionOccurred"/>.
    /// </summary>
    event EventHandler<J1939TpDatagram>? DatagramReceived;

    /// <summary>
    /// Raised when a background failure (session timeout on an idle receiver, event-handler
    /// exception, subscription failure, actor loop exception) must be surfaced to the
    /// application. Failures tied to a specific <see cref="SendBamAsync"/> /
    /// <see cref="SendCmAsync"/> call are reported via that call's returned task.
    /// </summary>
    event EventHandler<Exception>? BackgroundExceptionOccurred;
}
