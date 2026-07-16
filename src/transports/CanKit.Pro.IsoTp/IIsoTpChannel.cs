using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// A single ISO 15765-2 (ISO-TP) channel bound to one <see cref="IsoTpEndpoint"/>
/// (TX/RX CAN-ID pair, plus addressing mode). Threading model per SRS FR-TP-016 / FR-RAW-020..023:
/// all protocol state is owned by a single <see cref="Actor.IProtocolActor"/> mailbox, so callers
/// may invoke <see cref="SendAsync"/> concurrently from arbitrary threads and receive frames from
/// arbitrary threads; internal state is never mutated from more than one place at a time.
/// </summary>
/// <remarks>
/// <para>
/// One channel = one PDU at a time on the wire: an outgoing multi-frame PDU
/// (First-Frame + Consecutive-Frames) is completed (or aborted with an
/// <see cref="IsoTpException"/>) before the next <see cref="SendAsync"/> call is transmitted.
/// This mirrors ISO 15765-2 §6.4's "one N-USData at a time" model; overlapping sends from
/// different callers are serialized by the channel.
/// </para>
/// <para>
/// Received PDUs are delivered both as an event (<see cref="DatagramReceived"/>) and as an
/// <see cref="IAsyncEnumerable{T}"/> from <see cref="ReceiveAllAsync"/>; a single
/// <see cref="ReceiveAsync"/> call awaits the next one. Both surfaces share the same bounded
/// buffer.
/// </para>
/// <para>
/// <see cref="IDisposable.Dispose"/> is thread-safe and idempotent (FR-RAW-021).
/// </para>
/// </remarks>
public interface IIsoTpChannel : IDisposable
{
    /// <summary>The endpoint this channel is bound to (TX/RX CAN-ID pair + addressing mode).</summary>
    IsoTpEndpoint Endpoint { get; }

    /// <summary>The channel options used at construction (immutable).</summary>
    IsoTpChannelOptions Options { get; }

    /// <summary>
    /// Sends <paramref name="pdu"/> as one ISO-TP N-USData PDU. The returned task completes when
    /// the last frame of the PDU is TX-confirmed by the driver (SF) or when the peer has FC-cleared
    /// the entire multi-frame PDU and the last CF is confirmed (FF+CFs). It faults with an
    /// <see cref="IsoTpException"/> on protocol timeouts (FR-TP-010), an Overflow FC (FR-TP-012),
    /// exceeding WFTmax (FR-TP-011), or a driver rejection.
    /// </summary>
    /// <param name="pdu">User data, 1..4095 bytes for classic CAN, or up to
    /// <see cref="IsoTpFrameCodec.MaxFdFirstFrameLength"/> for CAN-FD. Must not be empty.</param>
    /// <param name="cancellationToken">Cancels the returned task (standard .NET convention); a
    /// canceled send aborts the in-flight PDU and returns the channel to idle so the next call
    /// can proceed.</param>
    Task SendAsync(ReadOnlyMemory<byte> pdu, CancellationToken cancellationToken = default);

    /// <summary>
    /// Awaits the next fully reassembled inbound PDU. Cancels via <paramref name="cancellationToken"/>.
    /// </summary>
    Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates every fully reassembled inbound PDU as it becomes available. The enumeration
    /// ends when the channel is disposed. Cancel by passing a token via <c>WithCancellation</c>
    /// or by disposing the channel.
    /// </summary>
    IAsyncEnumerable<byte[]> ReceiveAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised on the actor's loop thread every time a full PDU is reassembled. The same PDU is
    /// also enqueued for <see cref="ReceiveAsync"/>/<see cref="ReceiveAllAsync"/>. Handlers must
    /// be lightweight and non-throwing; a throwing handler is caught and surfaced via
    /// <see cref="BackgroundExceptionOccurred"/>.
    /// </summary>
    event EventHandler<IsoTpDatagramReceivedEventArgs>? DatagramReceived;

    /// <summary>
    /// Raised when a background failure (protocol timeout on an idle receiver, event-handler
    /// exception, subscription failure, actor loop exception) needs to be surfaced to the
    /// application. This is the single documented channel for out-of-band errors (FR-RAW-023);
    /// failures tied to a specific <see cref="SendAsync"/> call are still reported via that
    /// call's returned task.
    /// </summary>
    event EventHandler<Exception>? BackgroundExceptionOccurred;
}
