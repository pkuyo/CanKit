using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Pro.Actor;
using CanKit.Pro.RawCan;
using CanKit.Pro.Reliability;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// Actor-driven <see cref="IIsoTpChannel"/> that composes on top of the CanKit.Pro L2 services:
/// <see cref="ICanBusService"/> for RX demux and TX-confirm, <see cref="IProtocolActor"/> for
/// single-writer state, <see cref="DeadlineScheduler"/> for N_As/N_Bs/N_Cr timers.
/// </summary>
/// <remarks>
/// <para>Threading model (SRS FR-TP-016, FR-RAW-020..023):</para>
/// <list type="bullet">
/// <item><description>Every protocol-state field (TX operation, RX buffer, sequence numbers,
/// block counters, deadlines) lives inside the actor and is only ever read/written on the
/// actor's loop thread — no locks needed inside the state machines.</description></item>
/// <item><description>RX frames from the demux subscription are marshaled onto the actor via
/// <see cref="IProtocolActor.Post(Action)"/>; TX confirmations from
/// <see cref="ICanBusService.SendConfirmed"/> run on the thread-pool and post their outcome back
/// onto the actor.</description></item>
/// <item><description>The subscription reader is a single <see cref="Task"/> that ends when the
/// subscription completes (i.e. the channel or the owning service is disposed) — no busy loop
/// (FR-RAW-022).</description></item>
/// <item><description>Any exception raised by an event handler or a scheduled callback is
/// surfaced via <see cref="BackgroundExceptionOccurred"/> (FR-RAW-023).</description></item>
/// </list>
/// <para>The channel does not construct <see cref="CanFrame"/> instances that own memory:
/// frames are built with plain <see cref="ReadOnlyMemory{Byte}"/> payloads (backed by
/// stack- or heap-allocated arrays), so there is no <see cref="IDisposable"/> ownership to
/// forward to <see cref="ICanBusService.SendConfirmed"/> — matching the plain-payload variant
/// of the frame factory.</para>
/// </remarks>
internal sealed class IsoTpChannel : IIsoTpChannel
{
    private readonly ICanBusService _service;
    private readonly bool _ownsService;
    private readonly IsoTpEndpoint _endpoint;
    private readonly IsoTpChannelOptions _options;

    private readonly ProtocolActor _actor;
    private readonly DeadlineScheduler _deadlines;
    private readonly ISubscription _subscription;
    private readonly Task _readerTask;
    private readonly CancellationTokenSource _readerCts = new();

    // Bounded receive inbox for consumers. Drop-oldest so a stalled reader never stalls the RX
    // state machine (mirrors the L2 Subscription policy). Items are either a fully reassembled
    // PDU or a reassembly-abort fault (N_Cr / SN mismatch) so a blocked ReceiveAsync completes
    // instead of hanging (Bugbot 3596134684 / FR-TP-010) — the FailTx analogue on the RX side.
    private readonly Channel<RxInboxItem> _pduInbox;

    // Serializes SendAsync callers: one outbound PDU on the wire at a time, per ISO 15765-2's
    // "one N-USData at a time" model. Also avoids competition for _tx state across calls.
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    // TX state (only accessed on the actor loop). All null/zero while idle.
    private TxState? _tx;

    // Count of SendFrameOnBus → SendConfirmed operations still outstanding (actor-loop only).
    // Cancelled SendAsync must hold _sendGate until this drains so a subsequent send cannot
    // interleave frames with an aborted PDU that already submitted work to the bus
    // (Bugbot 3596212788).
    private int _busTxInFlight;

    // Set when a SendAsync caller is waiting for _busTxInFlight to reach zero (actor-loop only).
    private TaskCompletionSource<object?>? _busTxIdleWaiter;

    // RX state (only accessed on the actor loop). Null while no multi-frame reassembly in flight.
    private RxState? _rx;

    private int _disposed;

    /// <summary>
    /// Maximum PDU length this channel will transmit or reassemble — the same codec limit
    /// outbound <see cref="SendAsync"/> enforces via <see cref="IsoTpFrameCodec"/>
    /// (classic ≤ 4095; CAN-FD ≤ <see cref="IsoTpFrameCodec.MaxFdFirstFrameLength"/> capped to
    /// <see cref="int.MaxValue"/>).
    /// </summary>
    private int MaxPduLength =>
        _options.UseCanFd
            ? (int)Math.Min(IsoTpFrameCodec.MaxFdFirstFrameLength, int.MaxValue)
            : IsoTpFrameCodec.MaxClassicFirstFrameLength;

    /// <inheritdoc />
    public IsoTpEndpoint Endpoint => _endpoint;

    /// <inheritdoc />
    public IsoTpChannelOptions Options => _options;

    /// <inheritdoc />
    public event EventHandler<IsoTpDatagramReceivedEventArgs>? DatagramReceived;

    /// <inheritdoc />
    public event EventHandler<Exception>? BackgroundExceptionOccurred;

    internal IsoTpChannel(ICanBusService service, IsoTpEndpoint endpoint,
        IsoTpChannelOptions options, bool ownsService)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _endpoint = endpoint;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ownsService = ownsService;

        var inboxOptions = new BoundedChannelOptions(Math.Max(1, _options.ReceiveBufferCapacity))
        {
            SingleReader = false,
            SingleWriter = true, // written only from the actor loop
            FullMode = BoundedChannelFullMode.DropOldest,
        };
        _pduInbox = Channel.CreateBounded<RxInboxItem>(inboxOptions);

        _actor = new ProtocolActor();
        _actor.BackgroundExceptionOccurred += OnActorBackgroundException;
        _deadlines = new DeadlineScheduler(_actor);

        try
        {
            var idFilter = CanIdFilter.Range(
                _endpoint.RxCanId, _endpoint.RxCanId,
                _endpoint.IsExtendedCanId ? CanFilterIDType.Extend : CanFilterIDType.Standard);
            _subscription = _service.Subscribe(idFilter);
        }
        catch
        {
            _actor.Dispose();
            throw;
        }

        _readerTask = Task.Run(RunReaderAsync);
    }

    /// <inheritdoc />
    public async Task SendAsync(ReadOnlyMemory<byte> pdu, CancellationToken cancellationToken = default)
    {
        if (pdu.Length == 0)
            throw new ArgumentException("ISO-TP PDU must be non-empty.", nameof(pdu));
        if (pdu.Length > MaxPduLength)
            throw new ArgumentOutOfRangeException(nameof(pdu), pdu.Length,
                $"ISO-TP PDU length must be at most {MaxPduLength} bytes for this channel's frame kind.");
        ThrowIfDisposed();

        // Serialize per-channel: peer state (SN, block, deadlines) is only valid for one PDU at
        // a time. A canceled wait leaves the previous PDU untouched -- exactly the standard
        // .NET-cancellation-of-a-queued-op semantics.
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pduBytes = pdu.ToArray();
            CancellationTokenRegistration ctr = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(static state =>
                {
                    var (self, t, ct) = ((IsoTpChannel, TaskCompletionSource<object?>, CancellationToken))state!;
                    self.CancelInFlightSend(t, ct);
                }, (this, tcs, cancellationToken))
                : default;

            try
            {
                _actor.Post(() => BeginSendOnLoop(pduBytes, tcs, cancellationToken));
            }
            catch (Exception ex)
            {
                ctr.Dispose();
                tcs.TrySetException(ex);
            }

            try
            {
                await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                ctr.Dispose();
                // Hold the gate until any SendConfirmed already submitted for this PDU finishes.
                // CancelInFlightSend completes the TCS immediately but must not let the next
                // SendAsync race frames onto the bus (Bugbot 3596212788). Skip on dispose:
                // no subsequent SendAsync can run, and bus-TX confirmations may never post
                // back onto a torn-down actor (Bugbot 3596468541).
                if (Volatile.Read(ref _disposed) == 0)
                    await WaitForBusTxIdleAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // Dispose may tear down the gate after FailTx unblocks us but before Release runs
            // (Bugbot 3596468541). Swallow ODE so shutdown stays clean.
            try { _sendGate.Release(); }
            catch (ObjectDisposedException) { /* channel disposed */ }
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (await _pduInbox.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_pduInbox.Reader.TryRead(out var item))
                return UnwrapInboxItem(item);
        }
        throw new InvalidOperationException("Channel is disposed; no more PDUs will arrive.");
    }

    /// <inheritdoc />
    public int DiscardPendingPdus()
    {
        // Abort actor-side multi-frame reassembly first so leftover CFs cannot finish and enqueue
        // a stale PDU after a higher-layer timeout/cancel (Bugbot 3596444314). Silent clear —
        // not AbortRx — so we do not raise BackgroundExceptionOccurred or enqueue a fault that
        // we would immediately drain. Post+wait so any CF already queued on the actor mailbox
        // observes _rx == null and drops (HandleRxConsecutiveFrame).
        var rxCleared = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _actor.Post(() =>
            {
                try
                {
                    _rx?.CancelDeadline();
                    _rx = null;
                }
                finally
                {
                    rxCleared.TrySetResult(null);
                }
            });
        }
        catch (ObjectDisposedException)
        {
            rxCleared.TrySetResult(null);
        }

        try { rxCleared.Task.GetAwaiter().GetResult(); }
        catch { /* channel tearing down */ }

        // Drain both PDU and AbortRx-fault items. Leaving a fault behind would poison the next
        // ReceiveAsync after a higher-layer timeout/cancel — the opposite of the reset intent.
        int discarded = 0;
        while (_pduInbox.Reader.TryRead(out _))
            discarded++;
        return discarded;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<byte[]> ReceiveAllAsync(CancellationToken cancellationToken = default)
        => ReadAllAsync(cancellationToken);

    private async IAsyncEnumerable<byte[]> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = _pduInbox.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
                yield return UnwrapInboxItem(item);
        }
    }

    private static byte[] UnwrapInboxItem(RxInboxItem item)
    {
        if (item.Error is not null)
            throw item.Error;
        return item.Pdu!;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // Order matters: stop pulling frames off the subscription so nothing new is queued to the
        // actor while it winds down, then complete the PDU inbox so pending ReceiveAsync callers
        // see graceful termination, then tear down the actor (which cancels any pending deadlines
        // by observing ObjectDisposedException on their next actor.Schedule call), then finally
        // release the demux subscription and, if owned, the service.
        try { _readerCts.Cancel(); } catch { /* nothing else to do */ }

        // Complete the inbox first so consumers awaiting ReceiveAsync/ReadAllAsync unblock,
        // *before* we tear down the reader task -- otherwise a consumer could observe a canceled
        // reader without any completion signal.
        _pduInbox.Writer.TryComplete();

        // Fail any in-flight SendAsync so its caller doesn't hang forever waiting for a TCS the
        // now-disposed actor will never complete. Also release any bus-TX idle waiter that would
        // otherwise block SendAsync's finally path after CancelInFlightSend.
        _actor.Post(() =>
        {
            var tx = _tx;
            _tx = null;
            tx?.Fail(new ObjectDisposedException(nameof(IsoTpChannel)));
            var idle = _busTxIdleWaiter;
            _busTxIdleWaiter = null;
            idle?.TrySetResult(null);
        });

        try { _readerTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* observed via task; not fatal */ }

        _subscription.Dispose();
        // Actor.Dispose drains the FailTx/idle-waiter post above (FinalDrain), so the in-flight
        // SendAsync can leave its await and enter WaitForBusTxIdleAsync / Release.
        _actor.Dispose();
        _readerCts.Dispose();

        // Do not dispose _sendGate while an in-flight SendAsync still holds it — Release would
        // then throw ObjectDisposedException (Bugbot 3596468541). Wait briefly for that caller
        // to finish its finally path; timed-out Wait leaves the gate held and Dispose still
        // proceeds (Release then no-ops via the catch in SendAsync).
        try { _sendGate.Wait(TimeSpan.FromSeconds(2)); }
        catch (ObjectDisposedException) { /* already disposed */ }
        try { _sendGate.Dispose(); }
        catch (ObjectDisposedException) { /* already disposed */ }

        if (_ownsService)
            _service.Dispose();
    }

    // -----------------------------------------------------------------------------------------
    // Subscription reader — one long-lived Task per channel that pushes RX frames onto the actor.
    // -----------------------------------------------------------------------------------------
    private async Task RunReaderAsync()
    {
        try
        {
            await foreach (var frame in _subscription.Frames.WithCancellation(_readerCts.Token)
                .ConfigureAwait(false))
            {
                // Copy defensively: CanFrameView.Data may reference a reused buffer once we
                // hand control back to the subscription, and the RX state machine will keep the
                // payload alive across await points via the reassembly buffer.
                var payload = frame.Data.ToArray();
                var addrExt = _endpoint.UsesAddressExtension;
                // Endpoint uses an address-extension byte and the first byte does not match:
                // skip this frame silently (matches ISO 15765-2 §5.2.4.4 semantics for a foreign
                // address-extension on the same CAN-ID).
                //
                // Fix (Bugbot 3594960802): filter on the *RX* address extension. For Extended
                // addressing that is the local node's source address (which the peer writes into
                // the AE byte when it addresses us), NOT our outbound target-address byte.
                // For Mixed addressing the two are the same, so this is unchanged there.
                if (addrExt && (payload.Length == 0 || payload[0] != _endpoint.RxAddressExtension))
                    continue;

                _actor.Post(() => HandleReceivedFrame(payload));
            }
        }
        catch (OperationCanceledException)
        {
            // expected on Dispose
        }
        catch (Exception ex)
        {
            RaiseBackgroundException(ex);
        }
    }

    private void RaiseBackgroundException(Exception ex)
    {
        try
        {
            BackgroundExceptionOccurred?.Invoke(this, ex);
        }
        catch
        {
            // A misbehaving subscriber must not tear down the channel.
        }
    }

    private void OnActorBackgroundException(object? sender, Exception ex) => RaiseBackgroundException(ex);

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(IsoTpChannel));
    }

    // -----------------------------------------------------------------------------------------
    // TX side (all methods run on the actor loop unless noted)
    // -----------------------------------------------------------------------------------------

    private void BeginSendOnLoop(byte[] pdu, TaskCompletionSource<object?> tcs, CancellationToken ct)
    {
        // Fix (Bugbot 3594960794): the send may have been canceled between when the caller
        // posted us and when the actor got around to running us -- CancelInFlightSend may have
        // even already run (it saw _tx==null so it just canceled `tcs`). In either case the
        // outbound frame must never hit the wire; TrySetCanceled is a no-op if the TCS is
        // already completed.
        if (tcs.Task.IsCompleted || ct.IsCancellationRequested)
        {
            tcs.TrySetCanceled(ct);
            return;
        }

        if (_tx is not null)
        {
            // Sender gate already serializes SendAsync, so _tx must be null when we get here.
            // If it isn't, something has gone catastrophically wrong; fail this send rather
            // than corrupt the state machine.
            tcs.TrySetException(new InvalidOperationException(
                "Internal error: another ISO-TP send is already in flight (send-gate leaked)."));
            return;
        }

        _tx = new TxState(pdu, tcs);

        // Fix (Bugbot 3594960783): any synchronous throw from the codec (bad endpoint / bad
        // length / etc.) must clear _tx and fail the TCS. Otherwise the actor's own
        // BackgroundException handler swallows the throw, the awaiting SendAsync never
        // completes, and _tx stays pinned to the dead operation -- so the next SendAsync
        // sees the "gate leaked" branch above forever.
        try
        {
            int sfMax = IsoTpFrameCodec.SingleFrameMaxDataLength(_options.UseCanFd, _endpoint.UsesAddressExtension);
            if (pdu.Length <= sfMax)
            {
                SendSingleFrame();
            }
            else
            {
                SendFirstFrame();
            }
        }
        catch (Exception ex)
        {
            FailTx(ex);
        }
    }

    private void SendSingleFrame()
    {
        var tx = _tx!;
        var payload = IsoTpFrameCodec.BuildSingleFrame(_endpoint, tx.Pdu.AsSpan(),
            _options.UseCanFd, _options.UsePadding, _options.PaddingByte);
        SendFrameOnBus(payload, expectTx: TxExpect.SingleFrameConfirm);
    }

    private void SendFirstFrame()
    {
        var tx = _tx!;
        bool useLong = tx.Pdu.Length > IsoTpFrameCodec.MaxClassicFirstFrameLength;
        int ffData = IsoTpFrameCodec.FirstFrameMaxDataLength(_options.UseCanFd,
            _endpoint.UsesAddressExtension, useLong);
        if (ffData <= 0)
        {
            FailTx(new InvalidOperationException(
                "ISO-TP first-frame data capacity is non-positive for the configured endpoint/frame kind."));
            return;
        }

        var firstChunk = tx.Pdu.AsSpan(0, Math.Min(ffData, tx.Pdu.Length));
        var payload = IsoTpFrameCodec.BuildFirstFrame(_endpoint, tx.Pdu.Length, firstChunk,
            _options.UseCanFd);

        tx.Offset = firstChunk.Length;
        tx.NextSn = IsoTpFrameCodec.FirstConsecutiveSequenceNumber;
        tx.State = TxStage.WaitFcInitial;
        tx.WaitFramesReceived = 0;
        ArmNBs();
        SendFrameOnBus(payload, expectTx: TxExpect.FirstFrameConfirm);
    }

    private void SendNextConsecutiveFrame()
    {
        var tx = _tx;
        if (tx is null) return; // canceled/failed while STmin scheduled

        int cfCap = IsoTpFrameCodec.ConsecutiveFrameMaxDataLength(_options.UseCanFd,
            _endpoint.UsesAddressExtension);
        int remaining = tx.Pdu.Length - tx.Offset;
        int chunkLen = Math.Min(cfCap, remaining);
        var chunk = tx.Pdu.AsSpan(tx.Offset, chunkLen);
        var payload = IsoTpFrameCodec.BuildConsecutiveFrame(_endpoint, tx.NextSn, chunk,
            _options.UseCanFd, _options.UsePadding, _options.PaddingByte);
        tx.LastCfChunkLen = chunkLen;
        tx.State = TxStage.SendingCf;
        SendFrameOnBus(payload, expectTx: TxExpect.ConsecutiveFrameConfirm);
    }

    private enum TxExpect
    {
        SingleFrameConfirm,
        FirstFrameConfirm,
        ConsecutiveFrameConfirm,
    }

    // Non-blocking fire: SendConfirmed runs on the thread pool and posts its outcome back on the
    // actor. The actor loop is never blocked on a Task.await (SendConfirmed can wait up to N_As
    // for an echo). Both the success handler and the fault handler are named methods on TxState
    // so a canceled/timed-out send that we already failed doesn't touch _tx twice.
    private void SendFrameOnBus(byte[] payload, TxExpect expectTx)
    {
        var frame = _options.UseCanFd
            ? CanFrame.Fd(unchecked((int)_endpoint.TxCanId), payload,
                isExtendedFrame: _endpoint.IsExtendedCanId)
            : CanFrame.Classic(unchecked((int)_endpoint.TxCanId), payload,
                isExtendedFrame: _endpoint.IsExtendedCanId);

        // Capture the current TX for closure identity: if _tx has been replaced by a subsequent
        // send by the time confirmation lands, we must not touch that unrelated operation.
        var expected = _tx;
        var timeout = _options.NAs;

        // Increment before leaving the actor so WaitForBusTxIdleAsync never observes a gap
        // between "frame submitted" and "in-flight count visible".
        _busTxInFlight++;

        _ = Task.Run(async () =>
        {
            TxConfirmation? confirmation = null;
            Exception? failure = null;
            try
            {
                var c = await _service.SendConfirmed(frame, timeout).ConfigureAwait(false);
                confirmation = c;
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                _actor.Post(() =>
                {
                    try
                    {
                        OnSendConfirmed(expected, expectTx, confirmation, failure);
                    }
                    finally
                    {
                        NoteBusTxCompleted();
                    }
                });
            }
            catch (ObjectDisposedException)
            {
                // Actor is gone; the channel is tearing down. Dispose already releases any idle
                // waiter, so there is nothing left to synchronize with.
            }
        });
    }

    /// <summary>
    /// Actor-loop: one <see cref="SendFrameOnBus"/> confirmation has finished. Unblocks a
    /// <see cref="WaitForBusTxIdleAsync"/> waiter when the last in-flight bus TX drains.
    /// </summary>
    private void NoteBusTxCompleted()
    {
        if (_busTxInFlight > 0)
            _busTxInFlight--;
        if (_busTxInFlight == 0 && _busTxIdleWaiter is not null)
        {
            var waiter = _busTxIdleWaiter;
            _busTxIdleWaiter = null;
            waiter.TrySetResult(null);
        }
    }

    /// <summary>
    /// Completes when every <see cref="SendFrameOnBus"/> started for the current send has had
    /// its confirmation posted back onto the actor (or the channel/actor is already disposed).
    /// </summary>
    private Task WaitForBusTxIdleAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return Task.CompletedTask;

        var waiter = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _actor.Post(() =>
            {
                // Channel dispose may have raced past the Volatile check above; do not arm a
                // waiter that Dispose's FailTx post already missed (Bugbot 3596468541).
                if (Volatile.Read(ref _disposed) != 0 || _busTxInFlight == 0)
                {
                    waiter.TrySetResult(null);
                }
                else
                {
                    // Send gate serializes SendAsync, so at most one idle waiter exists.
                    _busTxIdleWaiter = waiter;
                }
            });
        }
        catch (ObjectDisposedException)
        {
            waiter.TrySetResult(null);
        }
        return waiter.Task;
    }

    private void OnSendConfirmed(TxState? expected, TxExpect kind, TxConfirmation? confirmation, Exception? failure)
    {
        // Late confirmation for a canceled/replaced/aborted TX: ignore.
        if (expected is null || !ReferenceEquals(expected, _tx))
            return;

        if (failure is not null)
        {
            FailTx(failure);
            return;
        }

        var conf = confirmation!.Value;
        if (!conf.Confirmed)
        {
            switch (conf.FailureReason)
            {
                case TxConfirmFailureReason.Timeout:
                    FailTx(new IsoTpTimeoutException(IsoTpTimer.NAs,
                        "N_As timer expired waiting for CAN driver TX confirmation."));
                    break;
                case TxConfirmFailureReason.BusOff:
                    FailTx(new IsoTpException("CAN bus went BusOff during ISO-TP transmission."));
                    break;
                case TxConfirmFailureReason.Rejected:
                    FailTx(new IsoTpSendRejectedException(
                        "CAN driver rejected the ISO-TP frame (Transmit returned 0)."));
                    break;
                default:
                    FailTx(new IsoTpException("ISO-TP TX confirmation failed with unknown reason."));
                    break;
            }
            return;
        }

        switch (kind)
        {
            case TxExpect.SingleFrameConfirm:
                CompleteTx();
                break;

            case TxExpect.FirstFrameConfirm:
                // We're already in WaitFcInitial with N_Bs armed; nothing else to do here. The
                // next event is either FC arriving on the actor via HandleReceivedFrame or N_Bs
                // expiring via OnNBsExpired.
                break;

            case TxExpect.ConsecutiveFrameConfirm:
                {
                    var tx = expected;
                    tx.Offset += tx.LastCfChunkLen;
                    tx.NextSn = IsoTpFrameCodec.NextConsecutiveSequenceNumber(tx.NextSn);

                    if (tx.Offset >= tx.Pdu.Length)
                    {
                        CompleteTx();
                        break;
                    }

                    // Block accounting: BS==0 => "everything in one block", never wait for FC.
                    if (tx.BlockSize != 0)
                    {
                        tx.CfsInCurrentBlock++;
                        if (tx.CfsInCurrentBlock >= tx.BlockSize)
                        {
                            tx.State = TxStage.WaitFcBlock;
                            tx.CfsInCurrentBlock = 0;
                            ArmNBs();
                            break;
                        }
                    }

                    ScheduleNextCf(tx);
                    break;
                }
        }
    }

    private void ScheduleNextCf(TxState tx)
    {
        if (tx.StMin > TimeSpan.Zero)
        {
            _actor.Schedule(tx.StMin, SendNextConsecutiveFrame);
        }
        else
        {
            SendNextConsecutiveFrame();
        }
    }

    private void ArmNBs()
    {
        var tx = _tx!;
        tx.NBsDeadline?.Dispose();
        tx.NBsDeadline = _deadlines.Arm(_options.NBs, OnNBsExpired);
    }

    private void OnNBsExpired()
    {
        var tx = _tx;
        if (tx is null) return;
        if (tx.State is not (TxStage.WaitFcInitial or TxStage.WaitFcBlock)) return;
        FailTx(new IsoTpTimeoutException(IsoTpTimer.NBs,
            "N_Bs timer expired waiting for peer Flow-Control frame."));
    }

    private void CompleteTx()
    {
        var tx = _tx;
        if (tx is null) return;
        tx.NBsDeadline?.Complete();
        _tx = null;
        tx.Tcs.TrySetResult(null);
    }

    private void FailTx(Exception ex)
    {
        var tx = _tx;
        if (tx is null) return;
        tx.NBsDeadline?.Complete();
        _tx = null;
        tx.Fail(ex);
    }

    private void CancelInFlightSend(TaskCompletionSource<object?> tcs, CancellationToken ct)
    {
        // Runs on whatever thread the CTS is cancelled from (thread-pool or user thread). Hop to
        // the actor so we don't race the state machine. Deliver the standard OperationCanceled
        // status regardless of whether the actor still has this exact TX active -- the caller
        // asked for cancellation and its awaited TCS must reflect that.
        try
        {
            _actor.Post(() =>
            {
                var tx = _tx;
                if (tx is not null && ReferenceEquals(tx.Tcs, tcs))
                {
                    tx.NBsDeadline?.Complete();
                    _tx = null;
                }
                tcs.TrySetCanceled(ct);
            });
        }
        catch (ObjectDisposedException)
        {
            tcs.TrySetCanceled(ct);
        }
    }

    // -----------------------------------------------------------------------------------------
    // RX side (all methods run on the actor loop)
    // -----------------------------------------------------------------------------------------

    private void HandleReceivedFrame(byte[] payload)
    {
        if (!IsoTpFrameCodec.TryParsePci(payload, _endpoint, out var pci))
            return; // truncated / reserved: drop silently (bounds-safe per FR-TP-007)

        switch (pci.Type)
        {
            case PciType.SingleFrame:
                HandleRxSingleFrame(payload, pci);
                break;
            case PciType.FirstFrame:
                HandleRxFirstFrame(payload, pci);
                break;
            case PciType.ConsecutiveFrame:
                HandleRxConsecutiveFrame(payload, pci);
                break;
            case PciType.FlowControl:
                HandleRxFlowControl(pci);
                break;
        }
    }

    private void HandleRxSingleFrame(byte[] payload, Pci pci)
    {
        // A racing SF starts a fresh PDU: abort any in-flight reassembly (matches ISO 15765-2
        // §6.5.2's "an unexpected N_PCI type shall abort reception").
        _rx?.CancelDeadline();
        _rx = null;
        if (pci.DataOffset + pci.Length > payload.Length) return; // codec already validates but guard again
        var pdu = new byte[pci.Length];
        Array.Copy(payload, pci.DataOffset, pdu, 0, pci.Length);
        EmitPdu(pdu);
    }

    private void HandleRxFirstFrame(byte[] payload, Pci pci)
    {
        // Discard any half-built reassembly -- ISO 15765-2 §6.5.5 says a new FF aborts.
        _rx?.CancelDeadline();
        _rx = null;

        // Cap reassembly allocation to the same outbound SendAsync / codec limit for this
        // frame kind (Bugbot 3596212802). A CAN-FD escape FF can announce up to int.MaxValue;
        // refuse with FC(OVFLW) and do not allocate.
        if (pci.Length < 1 || pci.Length > MaxPduLength)
        {
            SendOverflowFlowControl();
            return;
        }

        int firstChunk = payload.Length - pci.DataOffset;
        if (firstChunk < 0) firstChunk = 0;
        if (firstChunk > pci.Length) firstChunk = pci.Length;

        byte[] buffer;
        try
        {
            buffer = new byte[pci.Length];
        }
        catch (OutOfMemoryException)
        {
            // CAN-FD escape FF can announce up to int.MaxValue; if the process cannot honor the
            // codec max, refuse with OVFLW rather than faulting the actor loop.
            SendOverflowFlowControl();
            return;
        }
        Array.Copy(payload, pci.DataOffset, buffer, 0, firstChunk);

        // Reply with FC(CTS, BS, STmin) advertising our own block size / separation time.
        // ISO 15765-2 still expects an FC after FF even when the FF already carried the full
        // announced length (defensive path for peers that finish in one FF).
        var fc = IsoTpFrameCodec.BuildFlowControl(_endpoint, FlowStatus.ClearToSend,
            _options.LocalBlockSize, IsoTpFrameCodec.EncodeStMin(_options.LocalStMin),
            _options.UseCanFd, _options.UsePadding, _options.PaddingByte);
        SendUnsequencedFrame(fc);

        // If the FF already contains the full PDU, complete immediately — do not arm N_Cr
        // waiting for a CF that will never arrive.
        if (firstChunk >= pci.Length)
        {
            EmitPdu(buffer);
            return;
        }

        _rx = new RxState(buffer, received: firstChunk,
            expectedSn: IsoTpFrameCodec.FirstConsecutiveSequenceNumber,
            blockCounter: _options.LocalBlockSize);
        ArmNCr();
    }

    private void HandleRxConsecutiveFrame(byte[] payload, Pci pci)
    {
        var rx = _rx;
        if (rx is null) return; // stray CF, no reassembly in progress: drop per ISO 15765-2 §6.5.2.

        if (pci.SequenceNumber != rx.ExpectedSn)
        {
            // Sequence-number mismatch (FR-TP-002 negative case). Abort reception: surface via
            // BackgroundExceptionOccurred and fault any blocked ReceiveAsync (FailTx analogue)
            // so the waiter does not hang on _pduInbox (Bugbot 3596134684). The peer sender
            // will hit its own N_Cr / missing-FC path independently.
            AbortRx(new IsoTpException(
                $"ISO-TP CF sequence-number mismatch: expected {rx.ExpectedSn}, got {pci.SequenceNumber}."));
            return;
        }

        int remaining = rx.Buffer.Length - rx.Received;
        int available = payload.Length - pci.DataOffset;
        int copy = Math.Min(remaining, Math.Max(0, available));
        // Empty CF (PCI-only / no user data): ignore without advancing SN, BS, or N_Cr.
        // Advancing ExpectedSn here would desync reassembly so a valid next CF mismatches
        // or stalls until N_Cr aborts (Bugbot 3596378393).
        if (copy == 0)
            return;

        Array.Copy(payload, pci.DataOffset, rx.Buffer, rx.Received, copy);
        rx.Received += copy;
        rx.ExpectedSn = IsoTpFrameCodec.NextConsecutiveSequenceNumber(rx.ExpectedSn);

        if (rx.Received >= rx.Buffer.Length)
        {
            rx.CancelDeadline();
            var pdu = rx.Buffer;
            _rx = null;
            EmitPdu(pdu);
            return;
        }

        // BS accounting: LocalBlockSize == 0 means "no further FCs required in this session"
        // (send everything, matching the peer's own semantics when BS=0). Otherwise, decrement
        // and send another FC once we've received a full block.
        if (_options.LocalBlockSize != 0)
        {
            rx.BlockCounter--;
            if (rx.BlockCounter <= 0)
            {
                var fc = IsoTpFrameCodec.BuildFlowControl(_endpoint, FlowStatus.ClearToSend,
                    _options.LocalBlockSize, IsoTpFrameCodec.EncodeStMin(_options.LocalStMin),
                    _options.UseCanFd, _options.UsePadding, _options.PaddingByte);
                SendUnsequencedFrame(fc);
                rx.BlockCounter = _options.LocalBlockSize;
            }
        }

        // Any CF (including the one we just wrote a fresh FC after) refreshes N_Cr for the next CF.
        rx.RearmDeadline(_deadlines, _options.NCr, OnNCrExpired);
    }

    private void HandleRxFlowControl(Pci pci)
    {
        var tx = _tx;
        if (tx is null || tx.State is not (TxStage.WaitFcInitial or TxStage.WaitFcBlock))
            return; // FC with no matching pending TX: drop (matches ISO 15765-2 §6.5.5.2).

        // Peer FC responded within N_Bs -> stop that timer.
        tx.NBsDeadline?.Complete();
        tx.NBsDeadline = null;

        switch (pci.FlowStatus)
        {
            case FlowStatus.ClearToSend:
                tx.BlockSize = pci.BlockSize;
                tx.StMin = pci.StMin;
                tx.CfsInCurrentBlock = 0;
                tx.WaitFramesReceived = 0;
                tx.State = TxStage.SendingCf;
                ScheduleNextCf(tx);
                break;

            case FlowStatus.Wait:
                tx.WaitFramesReceived++;
                if (tx.WaitFramesReceived > _options.WftMax)
                {
                    FailTx(new IsoTpWaitFrameLimitExceededException(
                        tx.WaitFramesReceived, _options.WftMax));
                    return;
                }
                // Wait state: re-arm N_Bs and stay in WaitFc* until CTS/Overflow arrives.
                ArmNBs();
                break;

            case FlowStatus.Overflow:
                FailTx(new IsoTpOverflowException(
                    "Peer indicated Flow-Control Overflow (FS=OVFLW); PDU too large for the receiver."));
                break;
        }
    }

    private void ArmNCr()
    {
        var rx = _rx;
        if (rx is null) return;
        rx.RearmDeadline(_deadlines, _options.NCr, OnNCrExpired);
    }

    private void OnNCrExpired()
    {
        if (_rx is null) return;
        // FR-TP-010: N_Cr expiry must complete the receive with an observable error — not only
        // BackgroundExceptionOccurred (which leaves ReceiveAsync parked on an empty inbox).
        AbortRx(new IsoTpTimeoutException(IsoTpTimer.NCr,
            "N_Cr timer expired waiting for next Consecutive Frame."));
    }

    /// <summary>
    /// Aborts in-flight multi-frame reassembly. Mirrors <see cref="FailTx"/> on the receive side:
    /// clears RX state, raises <see cref="BackgroundExceptionOccurred"/>, and enqueues the fault
    /// into the PDU inbox so a blocked <see cref="ReceiveAsync"/>/<see cref="ReceiveAllAsync"/>
    /// completes with the same exception instead of waiting indefinitely (Bugbot 3596134684).
    /// Successful PDUs already in the inbox are unaffected (FIFO); exactly one waiter consumes
    /// the fault item, preserving multi-receive inbox semantics for subsequent PDUs.
    /// </summary>
    private void AbortRx(Exception ex)
    {
        var rx = _rx;
        if (rx is not null)
        {
            rx.CancelDeadline();
            _rx = null;
        }

        RaiseBackgroundException(ex);
        _pduInbox.Writer.TryWrite(RxInboxItem.FromError(ex));
    }

    private void SendOverflowFlowControl()
    {
        var ovflw = IsoTpFrameCodec.BuildFlowControl(_endpoint, FlowStatus.Overflow,
            blockSize: 0, stMinRaw: 0,
            _options.UseCanFd, _options.UsePadding, _options.PaddingByte);
        SendUnsequencedFrame(ovflw);
    }

    // Fire-and-forget send of a frame that is NOT part of the current TX PDU (typically a FC we
    // emit while receiving). Confirmation failures don't fail an outbound PDU; they surface as
    // background exceptions instead so a broken FC doesn't kill an unrelated in-flight send.
    private void SendUnsequencedFrame(byte[] payload)
    {
        var frame = _options.UseCanFd
            ? CanFrame.Fd(unchecked((int)_endpoint.TxCanId), payload,
                isExtendedFrame: _endpoint.IsExtendedCanId)
            : CanFrame.Classic(unchecked((int)_endpoint.TxCanId), payload,
                isExtendedFrame: _endpoint.IsExtendedCanId);
        var timeout = _options.NAs;

        _ = Task.Run(async () =>
        {
            try
            {
                var conf = await _service.SendConfirmed(frame, timeout).ConfigureAwait(false);
                if (!conf.Confirmed)
                {
                    RaiseBackgroundException(new IsoTpException(
                        $"ISO-TP unsequenced-frame send failed: {conf.FailureReason}."));
                }
            }
            catch (Exception ex)
            {
                RaiseBackgroundException(ex);
            }
        });
    }

    private void EmitPdu(byte[] pdu)
    {
        // Fire event first, then enqueue: this matches "sync consumers see it first" ordering
        // and mirrors how RawCan Subscription pushes into its channel after its own fan-out.
        try
        {
            DatagramReceived?.Invoke(this, new IsoTpDatagramReceivedEventArgs(_endpoint, pdu));
        }
        catch (Exception ex)
        {
            RaiseBackgroundException(ex);
        }
        _pduInbox.Writer.TryWrite(RxInboxItem.FromPdu(pdu));
    }

    // -----------------------------------------------------------------------------------------
    // Nested types
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// One inbox delivery: either a reassembled PDU or a reassembly-abort fault.
    /// </summary>
    private readonly struct RxInboxItem
    {
        private RxInboxItem(byte[]? pdu, Exception? error)
        {
            Pdu = pdu;
            Error = error;
        }

        public byte[]? Pdu { get; }
        public Exception? Error { get; }

        public static RxInboxItem FromPdu(byte[] pdu) => new(pdu, null);
        public static RxInboxItem FromError(Exception error) => new(null, error);
    }

    private enum TxStage
    {
        SingleOrFirstInFlight = 0,
        WaitFcInitial = 1,
        SendingCf = 2,
        WaitFcBlock = 3,
    }

    private sealed class TxState
    {
        public TxState(byte[] pdu, TaskCompletionSource<object?> tcs)
        {
            Pdu = pdu;
            Tcs = tcs;
            State = TxStage.SingleOrFirstInFlight;
        }

        public byte[] Pdu { get; }
        public TaskCompletionSource<object?> Tcs { get; }

        public TxStage State { get; set; }
        public int Offset { get; set; }
        public byte NextSn { get; set; }
        public int LastCfChunkLen { get; set; }

        // Peer-provided from the last CTS-FC.
        public byte BlockSize { get; set; }
        public TimeSpan StMin { get; set; }

        // Wait frame tracking (FR-TP-011).
        public int WaitFramesReceived { get; set; }
        public int CfsInCurrentBlock { get; set; }

        public IDeadline? NBsDeadline { get; set; }

        public void Fail(Exception ex)
        {
            NBsDeadline?.Dispose();
            NBsDeadline = null;
            Tcs.TrySetException(ex);
        }
    }

    private sealed class RxState
    {
        public RxState(byte[] buffer, int received, byte expectedSn, int blockCounter)
        {
            Buffer = buffer;
            Received = received;
            ExpectedSn = expectedSn;
            BlockCounter = blockCounter;
        }

        public byte[] Buffer { get; }
        public int Received { get; set; }
        public byte ExpectedSn { get; set; }
        public int BlockCounter { get; set; }
        public IDeadline? Deadline { get; private set; }

        public void RearmDeadline(DeadlineScheduler scheduler, TimeSpan timeout, Action onExpired)
        {
            var existing = Deadline;
            if (existing is not null && !existing.IsExpired && !existing.IsCancelled)
            {
                if (existing.Rearm(timeout)) return;
            }
            existing?.Dispose();
            Deadline = scheduler.Arm(timeout, onExpired);
        }

        public void CancelDeadline()
        {
            Deadline?.Dispose();
            Deadline = null;
        }
    }
}
