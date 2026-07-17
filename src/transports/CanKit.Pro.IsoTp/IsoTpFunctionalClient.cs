using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Pro.RawCan;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// ISO-TP functional (1:N broadcast) client per ISO 15765-2 §9 / ISO 14229-1 §7.5.4.
/// </summary>
/// <remarks>
/// <para>
/// Functional addressing allows a tester to broadcast a Single-Frame request on a shared
/// functional CAN identifier and collect Single-Frame responses from multiple ECUs, each
/// replying on its own physical response CAN identifier.
/// </para>
/// <para>
/// <strong>Send restriction:</strong> ISO 15765-2 §9.4 requires that functional requests are
/// Single Frames only. Attempting to send a PDU that exceeds the Single-Frame capacity for
/// the configured frame kind faults the returned task with
/// <see cref="InvalidOperationException"/>. This is intentional: the standard prohibits
/// multi-frame functional requests because there is no dedicated physical address to which
/// each ECU could send a Flow-Control frame.
/// </para>
/// <para>
/// <strong>Response collection:</strong> <see cref="SendAndCollectAsync"/> and
/// <see cref="CollectResponsesAsync"/> collect Single-Frame responses that arrive within a
/// caller-supplied time window. First-Frame responses are not reassembled (they would require
/// the tester to know each ECU's physical response-to-request addressing, which is not
/// available in the general functional-addressing case); they are silently dropped. Flow-Control
/// and Consecutive-Frame messages are likewise dropped.
/// </para>
/// <para>
/// <strong>Threading:</strong> <see cref="SendAndCollectAsync"/>, <see cref="SendAsync"/>, and
/// <see cref="CollectResponsesAsync"/> are safe to call from any thread. They are not mutually
/// concurrent — only one call at a time is meaningful because each creates a fresh subscription
/// window; callers that need to serialise multiple rounds should await each call in turn.
/// <see cref="Dispose"/> is thread-safe and idempotent.
/// </para>
/// </remarks>
public sealed class IsoTpFunctionalClient : IDisposable
{
    private readonly ICanBusService _service;
    private readonly bool _ownsService;
    private readonly IsoTpFunctionalOptions _options;

    // Endpoint used only for building the outbound SF payload (Normal addressing, no AE byte).
    private readonly IsoTpEndpoint _txEndpoint;

    // CanIdFilter used for all response-collection subscriptions.
    private readonly CanIdFilter _responseFilter;

    // Shared dummy parsing endpoint for Normal addressing (no AE byte) — used in TryParsePci.
    private static readonly IsoTpEndpoint NormalParseEndpoint = IsoTpEndpoint.Normal(0, 0);

    private int _disposed;

    internal IsoTpFunctionalClient(
        ICanBusService service,
        uint functionalTxCanId,
        uint responseRxCanIdRangeStart,
        uint responseRxCanIdRangeEnd,
        IsoTpFunctionalOptions options,
        bool ownsService)
    {
        if (responseRxCanIdRangeEnd < responseRxCanIdRangeStart)
            throw new ArgumentOutOfRangeException(nameof(responseRxCanIdRangeEnd),
                "responseRxCanIdRangeEnd must be greater than or equal to responseRxCanIdRangeStart.");

        _service = service ?? throw new ArgumentNullException(nameof(service));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ownsService = ownsService;

        _txEndpoint = IsoTpEndpoint.Normal(functionalTxCanId, 0, options.IsExtendedCanId);

        var idType = options.IsExtendedCanId
            ? CanFilterIDType.Extend
            : CanFilterIDType.Standard;
        _responseFilter = CanIdFilter.Range(responseRxCanIdRangeStart, responseRxCanIdRangeEnd, idType);
    }

    /// <summary>The functional TX CAN identifier used for outbound Single Frames.</summary>
    public uint FunctionalTxCanId => _txEndpoint.TxCanId;

    /// <summary>The options this client was opened with.</summary>
    public IsoTpFunctionalOptions Options => _options;

    /// <summary>
    /// Sends <paramref name="pdu"/> as a Single Frame on the functional CAN identifier, then
    /// collects Single-Frame responses from any ECU whose response CAN-ID falls within the
    /// configured range, returning all responses received within <paramref name="window"/>.
    /// </summary>
    /// <param name="pdu">
    /// User-data payload. Must fit in one ISO-TP Single Frame (≤ 7 bytes for classic CAN without
    /// address extension, ≤ 62 bytes for CAN-FD). Violating this limit faults the task with
    /// <see cref="InvalidOperationException"/> — multi-frame functional requests are prohibited
    /// by ISO 15765-2 §9.4.
    /// </param>
    /// <param name="window">
    /// Duration to wait for responses after the Single Frame is TX-confirmed. Expired windows
    /// return all responses collected so far; they do not fault the task.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels both the TX-confirm wait and the response-collection window.
    /// </param>
    /// <returns>
    /// All Single-Frame responses received within the window, in arrival order.
    /// May be empty if no ECU responded.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="pdu"/> exceeds the Single-Frame capacity for the configured frame kind.
    /// </exception>
    /// <exception cref="IsoTpException">
    /// The TX-confirm failed (bus-off, driver rejection, or N_As timeout).
    /// </exception>
    public async Task<IReadOnlyList<IsoTpFunctionalResponse>> SendAndCollectAsync(
        ReadOnlyMemory<byte> pdu,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (pdu.Length == 0)
            throw new ArgumentException("ISO-TP PDU must be non-empty.", nameof(pdu));

        // Subscribe before sending so no early fast response is missed. Every frame that
        // arrives on the response filter *before* we hand the outbound Single Frame to the
        // driver is stale bus traffic (a previous request's late reply, background chatter,
        // an unrelated ECU broadcast, …) and MUST NOT be treated as a reply to the request
        // we are about to send.
        //
        // The gate below is signalled from inside SendSingleFrameAsync, immediately before
        // it calls the driver — anything the subscription has already buffered by that
        // point is dropped; anything that arrives afterwards is a candidate reply and is
        // collected until the caller-supplied window expires. Doing this "just before the
        // driver call" (not "after TX-confirm returns") is important: on very fast buses
        // (e.g. the in-process Virtual bus, or a hardware ECU with a near-zero P2 time)
        // the ECU's reply can already be on our RX subscription before the outbound
        // TX-echo confirmation arrives, and gating on TX-confirm would then discard the
        // legitimate reply.
        using var sub = _service.Subscribe(_responseFilter);
        var gate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var collectTask = CollectAfterGateAsync(sub, gate.Task, window, cancellationToken);

        try
        {
            await SendSingleFrameAsync(pdu, gate, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Ensure the collector unwinds instead of running the whole window on an
            // aborted send. If the gate was already opened (send failed after TX started)
            // we still let the collector complete gracefully — cancel via TrySetCanceled
            // only matters when the gate never opened, in which case the collector was
            // idling in the pre-gate discard phase and needs to bail out immediately.
            gate.TrySetCanceled();
            try { await collectTask.ConfigureAwait(false); }
            catch { /* observe & swallow — the original send failure is what the caller sees */ }
            throw;
        }

        return await collectTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Sends <paramref name="pdu"/> as a Single Frame on the functional CAN identifier. The
    /// returned task completes when the CAN driver confirms the frame was accepted.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="SendAndCollectAsync"/> for the typical request/collect pattern, as it
    /// subscribes to the response range before sending so that no fast ECU response is missed.
    /// </remarks>
    /// <param name="pdu">User-data payload. Must fit in a Single Frame (see class remarks).</param>
    /// <param name="cancellationToken">Cancels the TX-confirm wait.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="pdu"/> exceeds the Single-Frame capacity.
    /// </exception>
    /// <exception cref="IsoTpException">TX-confirm failed.</exception>
    public Task SendAsync(ReadOnlyMemory<byte> pdu, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (pdu.Length == 0)
            throw new ArgumentException("ISO-TP PDU must be non-empty.", nameof(pdu));
        return SendSingleFrameAsync(pdu, cancellationToken);
    }

    /// <summary>
    /// Creates a fresh response subscription and collects Single-Frame responses from any ECU
    /// whose response CAN-ID falls within the configured range for the duration of
    /// <paramref name="window"/>.
    /// </summary>
    /// <remarks>
    /// When called after <see cref="SendAsync"/>, there is a small window between send and
    /// subscribe where a very fast ECU response may be missed. Use <see cref="SendAndCollectAsync"/>
    /// to eliminate that race.
    /// </remarks>
    /// <param name="window">Duration to collect responses.</param>
    /// <param name="cancellationToken">Cancels the collection window early.</param>
    /// <returns>All Single-Frame responses received within the window, in arrival order.</returns>
    public async Task<IReadOnlyList<IsoTpFunctionalResponse>> CollectResponsesAsync(
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var sub = _service.Subscribe(_responseFilter);
        return await CollectFromSubscriptionAsync(sub, window, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsService)
            _service.Dispose();
    }

    // -----------------------------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------------------------

    private Task SendSingleFrameAsync(ReadOnlyMemory<byte> pdu, CancellationToken ct)
        => SendSingleFrameAsync(pdu, sendStartedGate: null, ct);

    /// <summary>
    /// Sends <paramref name="pdu"/> as a functional Single Frame. If
    /// <paramref name="sendStartedGate"/> is non-null, it is completed immediately before the
    /// call to <see cref="ICanBusService.SendConfirmed"/>; <see cref="SendAndCollectAsync"/>
    /// uses this to demarcate stale frames buffered on the response subscription from
    /// candidate replies.
    /// </summary>
    private async Task SendSingleFrameAsync(ReadOnlyMemory<byte> pdu,
        TaskCompletionSource<object?>? sendStartedGate, CancellationToken ct)
    {
        int sfMax = IsoTpFrameCodec.SingleFrameMaxDataLength(_options.UseCanFd,
            _txEndpoint.UsesAddressExtension);

        if (pdu.Length > sfMax)
            throw new InvalidOperationException(
                $"ISO-TP functional addressing restricts outbound requests to Single Frames " +
                $"(ISO 15765-2 §9.4). The supplied PDU is {pdu.Length} bytes but the maximum " +
                $"Single-Frame payload for {(_options.UseCanFd ? "CAN-FD" : "classic CAN")} " +
                $"is {sfMax} bytes.");

        var payload = IsoTpFrameCodec.BuildSingleFrame(_txEndpoint, pdu.Span,
            _options.UseCanFd, _options.UsePadding, _options.PaddingByte);

        var frame = _options.UseCanFd
            ? CanFrame.Fd(unchecked((int)_txEndpoint.TxCanId), payload,
                isExtendedFrame: _options.IsExtendedCanId)
            : CanFrame.Classic(unchecked((int)_txEndpoint.TxCanId), payload,
                isExtendedFrame: _options.IsExtendedCanId);

        // Open the "send has started" gate the moment before we hand the frame to the driver.
        // From this instant onward, frames matched by the response filter are candidate
        // replies; anything the collector saw before this point was stale.
        sendStartedGate?.TrySetResult(null);

        var confirmation = await _service.SendConfirmed(frame, _options.NAs, ct)
            .ConfigureAwait(false);

        if (!confirmation.Confirmed)
        {
            throw confirmation.FailureReason switch
            {
                TxConfirmFailureReason.Timeout =>
                    new IsoTpTimeoutException(IsoTpTimer.NAs,
                        "N_As timer expired waiting for CAN driver TX confirmation of the functional Single Frame."),
                TxConfirmFailureReason.BusOff =>
                    new IsoTpException("CAN bus went BusOff during functional Single Frame transmission."),
                TxConfirmFailureReason.Rejected =>
                    new IsoTpSendRejectedException(
                        "CAN driver rejected the functional Single Frame (Transmit returned 0)."),
                _ =>
                    new IsoTpException("Functional Single Frame TX confirmation failed with unknown reason."),
            };
        }
    }

    private static async Task<IReadOnlyList<IsoTpFunctionalResponse>> CollectFromSubscriptionAsync(
        ISubscription sub, TimeSpan window, CancellationToken cancellationToken)
    {
        var responses = new List<IsoTpFunctionalResponse>();

        // Combine the caller's token with a deadline token so the window bounds the collection.
        using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        windowCts.CancelAfter(window);
        var windowToken = windowCts.Token;

        try
        {
            await foreach (var frame in sub.Frames.WithCancellation(windowToken).ConfigureAwait(false))
            {
                if (TryParseFunctionalResponse(frame, out var response))
                    responses.Add(response!);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Window expired normally — not a caller-initiated cancellation.
            // Return whatever was collected so far.
        }

        return responses.AsReadOnly();
    }

    /// <summary>
    /// Drains any frames that arrived on the subscription <em>before</em> <paramref name="gateTask"/>
    /// completes (they belong to earlier bus traffic and must not be treated as replies), then
    /// arms the <paramref name="window"/> deadline and collects the frames that arrive after
    /// the gate opens. If the gate is cancelled (send failed), the collector unwinds and returns
    /// whatever it has (typically empty).
    /// </summary>
    private static async Task<IReadOnlyList<IsoTpFunctionalResponse>> CollectAfterGateAsync(
        ISubscription sub, Task gateTask, TimeSpan window, CancellationToken cancellationToken)
    {
        var responses = new List<IsoTpFunctionalResponse>();

        // One lifetime CTS drives the enumerator: it is cancelled by the caller's token or,
        // once the gate opens, by the window timer we arm below.
        using var lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var enumerator = sub.Frames.GetAsyncEnumerator(lifetimeCts.Token);
        try
        {
            Task<bool> nextMove = enumerator.MoveNextAsync().AsTask();
            bool gateOpen = false;

            while (true)
            {
                if (!gateOpen)
                {
                    // Race the next-frame wait against the gate. Whichever finishes first
                    // determines whether the frame is pre- or post-gate.
                    var winner = await Task.WhenAny(nextMove, gateTask).ConfigureAwait(false);
                    if (winner == gateTask)
                    {
                        // Send failed → collector aborts and returns nothing new.
                        if (gateTask.IsCanceled) return responses.AsReadOnly();

                        gateOpen = true;
                        // From this instant we are collecting for the caller-supplied window.
                        lifetimeCts.CancelAfter(window);
                        // Fall through: nextMove is still in flight; we now treat its result
                        // as a real reply (or, if the timer fires first, an OCE we swallow).
                        continue;
                    }

                    // A frame arrived before the gate opened — discard it as stale traffic.
                    bool moved;
                    try { moved = await nextMove.ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    if (!moved) return responses.AsReadOnly();
                    nextMove = enumerator.MoveNextAsync().AsTask();
                    continue;
                }

                bool got;
                try
                {
                    got = await nextMove.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break; // window expired
                }
                if (!got) break;

                if (TryParseFunctionalResponse(enumerator.Current, out var response))
                    responses.Add(response!);
                nextMove = enumerator.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            try { await enumerator.DisposeAsync().ConfigureAwait(false); }
            catch (OperationCanceledException) { /* enumerator token tripped — cleanup only */ }
            catch (ObjectDisposedException) { /* subscription already disposed */ }
        }

        return responses.AsReadOnly();
    }

    private static bool TryParseFunctionalResponse(CanFrameView frame,
        out IsoTpFunctionalResponse? response)
    {
        var payload = frame.Data.ToArray();
        bool isCanFd = frame.FrameKind == CanFrameType.CanFd;

        // Parse the PCI using Normal addressing (no address-extension byte).
        if (!IsoTpFrameCodec.TryParsePci(payload, NormalParseEndpoint, isCanFd, out var pci))
        {
            response = null;
            return false;
        }

        // Functional addressing: collect only Single-Frame responses.
        // First-Frame responses cannot be reassembled without a physical TX address for
        // each ECU's Flow-Control reply (ISO 15765-2 §9.4). CF and FC are stray frames.
        if (pci.Type != PciType.SingleFrame)
        {
            response = null;
            return false;
        }

        if (pci.DataOffset + pci.Length > payload.Length)
        {
            response = null;
            return false;
        }

        var pdu = new byte[pci.Length];
        Array.Copy(payload, pci.DataOffset, pdu, 0, pci.Length);
        response = new IsoTpFunctionalResponse((uint)frame.ID, pdu);
        return true;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(IsoTpFunctionalClient));
    }
}
