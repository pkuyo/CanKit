using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Pro.Actor;
using CanKit.Pro.Addressing;
using CanKit.Pro.J1939Tp;
using CanKit.Pro.RawCan;
using CanKit.Pro.Reliability;

namespace CanKit.Pro.J1939;

/// <summary>
/// Actor-driven <see cref="IJ1939Node"/> implementation that composes on the CanKit.Pro L2
/// services (<see cref="ICanBusService"/>, <see cref="IProtocolActor"/>,
/// <see cref="DeadlineScheduler"/>) and delegates multi-frame (&gt; 8 bytes) payloads to a
/// shared <see cref="IJ1939TpChannel"/> per SRS FR-J1939-006.
/// </summary>
/// <remarks>
/// <para>
/// The node subscribes to every extended-ID frame on the bus and classifies each one on the
/// actor loop (single-writer discipline for all node state — claim state, address, pending
/// claim TCS): Address Claim / Cannot Claim (PGN 0xEE00) drives the state machine, Request-PGN
/// (0xEA00) and every other application PGN targeted at us (destination = our SA) or the
/// global address (0xFF) surfaces via <see cref="MessageReceived"/>. TP.CM / TP.DT frames go
/// through the shared J1939-TP channel and their reassembled datagrams show up as
/// <see cref="J1939Message"/> events too.
/// </para>
/// <para>
/// Outbound routing is entirely payload-length based (FR-J1939-006): &lt;= 8 bytes goes on the
/// wire as one 29-bit CAN frame; &gt; 8 bytes is handed to the transport channel which sends
/// TP.BAM for global destinations or TP.CM for a specific destination.
/// </para>
/// </remarks>
internal sealed class J1939NodeImpl : IJ1939Node
{
    private readonly ICanBusService _service;
    private readonly bool _ownsService;
    private readonly J1939NodeOptions _options;
    private readonly J1939Name _name;
    private readonly ProtocolActor _actor;
    private readonly DeadlineScheduler _deadlines;
    private readonly ISubscription _subscription;
    // The transport channel and its reader task are recreated whenever the node's claimed
    // address changes: J1939TpChannel uses its SourceAddress as the local channel identity and
    // accepts inbound TP frames whose destination address is that identity (or global 0xFF), so
    // after a successful claim we must re-open the channel on the claimed address (Bugbot
    // 3600377721). Mutation of these two fields only happens on the actor loop.
    private IJ1939TpChannel _transport = null!;
    private Task _transportReaderTask = null!;
    private readonly Task _readerTask;
    private readonly CancellationTokenSource _readerCts = new();
    private readonly Channel<J1939Message> _rxInbox;

    private PendingClaim? _pendingClaim;
    private int _disposed;

    /// <inheritdoc />
    public byte? Address
    {
        get
        {
            int v = Volatile.Read(ref _addressStore);
            return v < 0 ? null : (byte)v;
        }
    }

    // Backing store: byte doesn't have a null representation, so we use an int-alike.
    // A negative value means "unclaimed". Reads via Volatile so callers on any thread see the
    // most recently committed value; writes happen only on the actor loop.
    private int _addressStore = -1;

    /// <inheritdoc />
    public J1939Name Name => _name;

    /// <inheritdoc />
    public J1939ClaimState ClaimState => (J1939ClaimState)Volatile.Read(ref _claimStateStore);
    private int _claimStateStore;

    /// <inheritdoc />
    public J1939NodeOptions Options => _options;

    /// <inheritdoc />
    public event EventHandler<J1939Message>? MessageReceived;

    /// <inheritdoc />
    public event EventHandler<J1939ClaimEventArgs>? AddressClaimChanged;

    /// <inheritdoc />
    public event EventHandler<Exception>? BackgroundExceptionOccurred;

    internal J1939NodeImpl(ICanBusService service, J1939NodeOptions options, bool ownsService)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _ownsService = ownsService;
        _name = options.Name;

        var inboxOpts = new BoundedChannelOptions(Math.Max(1, _options.ReceiveBufferCapacity))
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        };
        _rxInbox = Channel.CreateBounded<J1939Message>(inboxOpts);

        _actor = new ProtocolActor();
        _actor.BackgroundExceptionOccurred += OnActorBackgroundException;
        _deadlines = new DeadlineScheduler(_actor);

        // The TP channel is initially bound to the null address 0xFE because the node does not
        // yet have a claimed address. J1939TpChannel filters inbound TP frames by destination
        // address (the PDU1 PS byte), accepting frames directed to its channel identity or the
        // global address (0xFF), so at 0xFE it still receives BAM traffic. Directed TP.CM to a
        // claimed address cannot arrive until we re-open the channel with that identity
        // (Bugbot 3600377721). RebindTransportOnLoop does exactly that at each ClaimState
        // transition; here we just seed the initial placeholder channel.
        try
        {
            _transport = J1939Tp.J1939Tp.Open(_service, sourceAddress: J1939Pgn.NullAddress,
                options: _options.TransportOptions, leaveOpen: true);
        }
        catch
        {
            _actor.Dispose();
            throw;
        }
        _transport.BackgroundExceptionOccurred += OnTransportBackgroundException;

        try
        {
            // Subscribe to every extended-ID frame and classify on the actor loop. A single
            // predicate-less subscription is simpler than three overlapping mask filters and
            // still allocation-light: CanFrameView is a readonly struct passed by ref via the
            // subscription's async enumerator (FR-RAW-010/011).
            _subscription = _service.Subscribe(f => f.IsExtendedFrame);
        }
        catch
        {
            _transport.Dispose();
            _actor.Dispose();
            throw;
        }

        _readerTask = Task.Run(RunReaderAsync);
        _transportReaderTask = StartTransportReader(_transport);
    }

    // =========================================================================================
    // Address Claim (SRS FR-J1939-003 / FR-J1939-004)
    // =========================================================================================

    /// <inheritdoc />
    public Task ClaimAddressAsync(byte preferredAddress, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (preferredAddress == J1939Pgn.GlobalAddress || preferredAddress == J1939Pgn.NullAddress)
            throw new ArgumentOutOfRangeException(nameof(preferredAddress),
                $"Preferred address must be in [0x00, 0xFD]; 0xFE (Null) and 0xFF (Global) are reserved.");

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration ctr = default;
        if (cancellationToken.CanBeCanceled)
        {
            // On user cancel we must both complete the returned task and tear down the pending
            // claim on the actor loop so `OnClaimAnnounceElapsed` cannot later commit the
            // preferred address after the caller already saw a cancellation (Bugbot
            // 3600440955). The actor post is best-effort — if the node has already been
            // disposed the mailbox is closed and there is no pending state to unwind anyway.
            ctr = cancellationToken.Register(static state =>
            {
                var box = (ClaimCancelState)state!;
                try
                {
                    box.Node._actor.Post(() => box.Node.CancelPendingClaimOnLoop(box.Tcs));
                }
                catch (ObjectDisposedException) { /* actor already gone */ }
                box.Tcs.TrySetCanceled();
            }, new ClaimCancelState(this, tcs));
        }

        _actor.Post(() => BeginClaim(preferredAddress, tcs, ctr));
        return tcs.Task;
    }

    // Runs on the actor loop. Tears down `_pendingClaim` when its owning caller cancels the
    // ClaimAddressAsync task, so the arbitration timer cannot later commit the address after
    // the caller has already observed a cancellation (Bugbot 3600440955).
    private void CancelPendingClaimOnLoop(TaskCompletionSource<object?> tcs)
    {
        if (_disposed != 0) return;
        var pending = _pendingClaim;
        if (pending is null)
        {
            // Bugbot 3600614141: OnClaimAnnounceElapsed can race the cancel post and consume
            // `_pendingClaim` on its early-return path (its check for TCS-already-completed
            // fires because the token registration called TrySetCanceled *before* posting
            // the cancel). In that case the deadline callback already cleared the pending
            // slot but returned without touching ClaimState, so without this defensive
            // sweep the node stays wedged in Claiming with no address. Rolling back to
            // NotClaimed here (BeginClaim already invalidated the address and rebound the
            // TP channel to 0xFE) is idempotent and safe if OnClaimAnnounceElapsed already
            // committed the same transition.
            if ((J1939ClaimState)Volatile.Read(ref _claimStateStore) == J1939ClaimState.Claiming)
            {
                WriteAddress(null);
                RebindTransportOnLoop(J1939Pgn.NullAddress);
                SetClaimState(J1939ClaimState.NotClaimed, address: null,
                    contendingSa: null, contendingName: null);
            }
            return;
        }
        // A newer ClaimAddressAsync may have replaced this pending claim already; in that case
        // the fresh claim owns the actor state and we must not disturb it.
        if (!ReferenceEquals(pending.Tcs, tcs)) return;

        _pendingClaim = null;
        pending.Deadline?.Dispose();
        pending.CtRegistration.Dispose();

        // BeginClaim already invalidated the address (WriteAddress(null)) and rebound the TP
        // channel back to the 0xFE placeholder before the arbitration announce, so rolling
        // the state machine back to NotClaimed on cancel is sufficient — there is no prior
        // Claimed state left to restore even for a re-claim on top of a previously-claimed
        // node.
        SetClaimState(J1939ClaimState.NotClaimed, address: null,
            contendingSa: null, contendingName: null);
        pending.Tcs.TrySetCanceled();
    }

    private void BeginClaim(byte preferredAddress, TaskCompletionSource<object?> tcs,
        CancellationTokenRegistration ctr)
    {
        if (_disposed != 0)
        {
            ctr.Dispose();
            tcs.TrySetException(new ObjectDisposedException(nameof(J1939NodeImpl)));
            return;
        }
        if (tcs.Task.IsCompleted)
        {
            ctr.Dispose();
            return;
        }

        // Cancel any previous in-flight claim.
        _pendingClaim?.Deadline?.Dispose();
        _pendingClaim?.Tcs.TrySetCanceled();
        _pendingClaim?.CtRegistration.Dispose();

        // Invalidate any previously-claimed address *before* announcing the new preferred SA:
        // application traffic must not race and go out on the old SA while the wire already
        // advertises a different preferred address. SendCoreAsync gates on ClaimState==Claimed
        // as well, but clearing the address here also makes the Address getter honest during
        // the arbitration window (Bugbot 3600377725).
        WriteAddress(null);
        // Publish Claiming *before* the (potentially long) transport rebind so observers never
        // see ClaimState==Claimed with Address==null during a re-claim (Bugbot 3600717316).
        SetClaimState(J1939ClaimState.Claiming, preferredAddress, contendingSa: null, contendingName: null);
        // Unbind the transport from any prior claimed address so we do not accept directed
        // TP.CM for the old address during the new arbitration window. Placeholder 0xFE still
        // receives broadcast TP.BAM traffic.
        RebindTransportOnLoop(J1939Pgn.NullAddress);

        // Register the pending claim *before* TX so contending peers that arrive during the
        // SendConfirmed await are still handled. Arm the arbitration deadline only after the
        // claim frame is confirmed on the bus — otherwise a failed/slow TX would still let
        // OnClaimAnnounceElapsed commit Claimed (Bugbot 3600799903).
        _pendingClaim = new PendingClaim(preferredAddress, tcs, deadline: null, ctr);
        TransmitAddressClaimConfirmed(sourceAddress: preferredAddress);
    }

    private void OnClaimAnnounceElapsed(byte preferredAddress)
    {
        var pending = _pendingClaim;
        if (pending is null || pending.PreferredAddress != preferredAddress) return;

        // If the caller already cancelled/faulted the returned task (racing between the
        // deadline callback and CancelPendingClaimOnLoop), do not commit the address — the
        // caller has already seen a non-success outcome (Bugbot 3600440955). The actor
        // serializes both callbacks, so this only catches the rare interleave where the
        // token registration set TrySetCanceled *before* posting the cancel, and the deadline
        // fired before the cancel post ran.
        if (pending.Tcs.Task.IsCompleted)
        {
            _pendingClaim = null;
            pending.Deadline?.Dispose();
            pending.CtRegistration.Dispose();
            // Bugbot 3600614141: BeginClaim moved us into Claiming and cleared the address /
            // rebound the TP to 0xFE. Because we are abandoning this pending claim without
            // committing, we MUST roll the state machine back to NotClaimed here — otherwise
            // the subsequent CancelPendingClaimOnLoop (or Dispose) sees no pending claim and
            // leaves ClaimState stuck at Claiming with no address.
            if ((J1939ClaimState)Volatile.Read(ref _claimStateStore) == J1939ClaimState.Claiming)
            {
                WriteAddress(null);
                RebindTransportOnLoop(J1939Pgn.NullAddress);
                SetClaimState(J1939ClaimState.NotClaimed, address: null,
                    contendingSa: null, contendingName: null);
            }
            return;
        }

        _pendingClaim = null;
        pending.Deadline?.Dispose();
        pending.CtRegistration.Dispose();

        // Nobody contested us within the arbitration window: commit the address.
        // Rebind the TP channel to the claimed address so directed TP.CM/TP.DT to us gets
        // accepted (the channel filters TP RX by destination address; a 0xFE placeholder drops
        // traffic directed to the claimed address — Bugbot 3600377721). If open fails after
        // disposing the placeholder channel, do NOT complete Claimed — multi-frame would be
        // broken while Address looks valid (Bugbot 3600825931).
        if (!RebindTransportOnLoop(preferredAddress))
        {
            // We already announced preferredAddress on the bus; peers may treat it as ours.
            // Broadcast Cannot-Claim (SA 0xFE) so the orphaned announcement is retracted
            // (Bugbot 3600845832), then leave the node unclaimed.
            WriteAddress(null);
            SetClaimState(J1939ClaimState.CannotClaim, address: null,
                contendingSa: null, contendingName: null);
            SendAddressClaimFrame(sourceAddress: J1939Pgn.NullAddress);
            pending.Tcs.TrySetException(new J1939NodeException(
                $"J1939 claim succeeded on the wire but TP rebind to SA 0x{preferredAddress:X2} failed."));
            return;
        }

        WriteAddress(preferredAddress);
        SetClaimState(J1939ClaimState.Claimed, preferredAddress, contendingSa: null, contendingName: null);
        pending.Tcs.TrySetResult(null);
    }

    private void OnClaimAnnounceTxConfirmed(byte preferredAddress)
    {
        var pending = _pendingClaim;
        if (pending is null || pending.PreferredAddress != preferredAddress) return;
        if (pending.Tcs.Task.IsCompleted) return;
        if (pending.Deadline is not null) return; // already armed (defensive)
        if ((J1939ClaimState)Volatile.Read(ref _claimStateStore) != J1939ClaimState.Claiming)
            return;

        // Arbitration window starts only after the claim announcement is on the wire.
        pending.Deadline = _deadlines.Arm(_options.ClaimAnnounceTimeout,
            () => OnClaimAnnounceElapsed(preferredAddress));
    }

    private void OnClaimAnnounceTxFailed(byte preferredAddress, Exception error)
    {
        var pending = _pendingClaim;
        if (pending is null || pending.PreferredAddress != preferredAddress) return;

        _pendingClaim = null;
        pending.Deadline?.Dispose();
        pending.CtRegistration.Dispose();

        if ((J1939ClaimState)Volatile.Read(ref _claimStateStore) == J1939ClaimState.Claiming)
        {
            WriteAddress(null);
            RebindTransportOnLoop(J1939Pgn.NullAddress);
            SetClaimState(J1939ClaimState.NotClaimed, address: null,
                contendingSa: null, contendingName: null);
        }

        pending.Tcs.TrySetException(error is J1939NodeException
            ? error
            : new J1939NodeException(
                $"J1939 address claim TX failed for SA 0x{preferredAddress:X2}.", error));
    }

    private void HandleIncomingAddressClaim(byte peerSa, byte[] payload)
    {
        if (payload.Length < 8) return; // malformed
        var peerName = J1939Name.Decompose(BitConverter.ToUInt64(payload, 0));

        // Own transmit echo (or an identical NAME on the bus) must not be treated as a losing
        // peer — equal NAME fails HasHigherClaimPriorityThan and would re-announce forever on
        // ChannelWorkMode.Echo adapters (Bugbot 3600783801). CanFrameView has no IsEcho bit,
        // so NAME equality is the reliable local-TX filter for Address Claim.
        if (peerName.Value == _name.Value) return;

        // A peer at SA=0xFE announces Cannot-Claim. Not directly relevant to *us* unless we
        // are in the middle of claiming — in which case a Cannot-Claim cannot contest us
        // (that peer has already lost).
        if (peerSa == J1939Pgn.NullAddress) return;

        var pending = _pendingClaim;
        if (pending is not null && peerSa == pending.PreferredAddress)
        {
            // Someone is contending our preferred address during the arbitration window.
            // SAE J1939-81 §4.4.3.2: numerically lower NAME wins.
            if (peerName.HasHigherClaimPriorityThan(_name))
            {
                // We lose. Enter CannotClaim and broadcast SA=0xFE with our NAME.
                _pendingClaim = null;
                pending.Deadline?.Dispose();
                pending.CtRegistration.Dispose();
                WriteAddress(null);
                // TP channel goes back to placeholder 0xFE — no directed TP traffic reaches
                // us while unclaimed.
                RebindTransportOnLoop(J1939Pgn.NullAddress);
                SetClaimState(J1939ClaimState.CannotClaim, address: null,
                    contendingSa: peerSa, contendingName: peerName);
                SendAddressClaimFrame(sourceAddress: J1939Pgn.NullAddress);
                pending.Tcs.TrySetException(new J1939CannotClaimException(pending.PreferredAddress));
                return;
            }

            // Peer's NAME is >= ours: they lose. Re-announce our own claim so they hear it,
            // then keep waiting on our deadline.
            SendAddressClaimFrame(sourceAddress: pending.PreferredAddress);
            return;
        }

        // We are already claimed at SA and a peer claims the same SA.
        if (ClaimState == J1939ClaimState.Claimed && _addressStore >= 0 && peerSa == (byte)_addressStore)
        {
            if (peerName.HasHigherClaimPriorityThan(_name))
            {
                // We are unseated. Broadcast Cannot-Claim and transition.
                WriteAddress(null);
                RebindTransportOnLoop(J1939Pgn.NullAddress);
                SetClaimState(J1939ClaimState.CannotClaim, address: null,
                    contendingSa: peerSa, contendingName: peerName);
                SendAddressClaimFrame(sourceAddress: J1939Pgn.NullAddress);
            }
            else
            {
                // Peer loses: re-announce our own claim so it moves off our address.
                SendAddressClaimFrame(sourceAddress: (byte)_addressStore);
            }
        }
    }

    private void SendAddressClaimFrame(byte sourceAddress)
    {
        // 8-byte little-endian NAME payload. PGN 0xEE00 is PDU1 with PS = 0xFF (global).
        // Re-announcements / Cannot-Claim remain fire-and-forget; the initial claim path uses
        // TransmitAddressClaimConfirmed so ClaimAddressAsync cannot succeed without TX confirm.
        var payload = BuildAddressClaimPayload();
        uint canId = J1939Id.ComposePgn(_options.ClaimPriority, J1939Pgn.AddressClaimed, sourceAddress,
            destinationAddress: J1939Pgn.GlobalAddress);
        TransmitFrame(canId, payload);
    }

    private byte[] BuildAddressClaimPayload()
    {
        var payload = new byte[8];
        ulong v = _name.Value;
        for (int i = 0; i < 8; i++) payload[i] = (byte)((v >> (8 * i)) & 0xFF);
        return payload;
    }

    /// <summary>
    /// Sends the initial Address Claim with <see cref="ICanBusService.SendConfirmed"/> and
    /// posts success/failure back onto the actor so the arbitration deadline is armed only
    /// after a confirmed TX (Bugbot 3600799903).
    /// </summary>
    private void TransmitAddressClaimConfirmed(byte sourceAddress)
    {
        var payload = BuildAddressClaimPayload();
        uint canId = J1939Id.ComposePgn(_options.ClaimPriority, J1939Pgn.AddressClaimed, sourceAddress,
            destinationAddress: J1939Pgn.GlobalAddress);
        byte preferred = sourceAddress;
        _ = Task.Run(async () =>
        {
            try
            {
                using var frame = CanFrame.Classic(unchecked((int)canId), payload, isExtendedFrame: true);
                var confirmation = await _service.SendConfirmed(frame).ConfigureAwait(false);
                if (!confirmation.Confirmed)
                {
                    var ex = new J1939NodeException(
                        $"J1939 address claim TX failed (id=0x{canId:X8}): {confirmation.FailureReason}.");
                    try { _actor.Post(() => OnClaimAnnounceTxFailed(preferred, ex)); }
                    catch (ObjectDisposedException) { }
                    return;
                }

                try { _actor.Post(() => OnClaimAnnounceTxConfirmed(preferred)); }
                catch (ObjectDisposedException) { }
            }
            catch (Exception ex)
            {
                try { _actor.Post(() => OnClaimAnnounceTxFailed(preferred, ex)); }
                catch (ObjectDisposedException) { }
            }
        });
    }

    private void SetClaimState(J1939ClaimState state, byte? address, byte? contendingSa,
        J1939Name? contendingName)
    {
        Volatile.Write(ref _claimStateStore, (int)state);
        var args = new J1939ClaimEventArgs(state, address, contendingSa, contendingName);
        try
        {
            AddressClaimChanged?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            RaiseBackgroundException(ex);
        }
    }

    private void WriteAddress(byte? address)
    {
        Volatile.Write(ref _addressStore, address.HasValue ? address.Value : -1);
    }

    // =========================================================================================
    // Send (SRS FR-J1939-001 / FR-J1939-005 / FR-J1939-006)
    // =========================================================================================

    /// <inheritdoc />
    public Task SendAsync(J1939Message message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return SendCoreAsync(message, cancellationToken);
    }

    private async Task SendCoreAsync(J1939Message message, CancellationToken cancellationToken)
    {
        if (message.Priority > 7)
            throw new ArgumentOutOfRangeException(nameof(message.Priority), message.Priority,
                "J1939 priority must be in [0, 7].");

        // Gate strictly on ClaimState==Claimed (Bugbot 3600377725): checking only that the
        // address store is non-negative would let application traffic go out on the previous
        // SA during a re-claim, while the wire already advertises a different preferred
        // address. BeginClaim clears the address as well, so both gates fail closed.
        if ((J1939ClaimState)Volatile.Read(ref _claimStateStore) != J1939ClaimState.Claimed)
            throw new J1939NoAddressException();
        int addr = Volatile.Read(ref _addressStore);
        if (addr < 0)
            throw new J1939NoAddressException();

        byte sa = (byte)addr;
        try
        {
            // Direct single-frame path (payload <= 8 bytes) — FR-J1939-006.
            if (message.Payload.Length <= 8)
            {
                uint canId = J1939Id.ComposePgn(message.Priority, message.Pgn, sa,
                    destinationAddress: message.DestinationAddress);
                var payload = new byte[message.Payload.Length];
                if (message.Payload.Length > 0) message.Payload.Span.CopyTo(payload);

                using var frame = CanFrame.Classic(unchecked((int)canId), payload, isExtendedFrame: true);
                var confirmation = await _service.SendConfirmed(frame, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!confirmation.Confirmed)
                    throw new J1939NodeException(
                        $"J1939 send failed for PGN 0x{message.Pgn:X}: {confirmation.FailureReason}.");
            }
            else
            {
                // Multi-frame (> 8 bytes) path via the shared J1939-TP channel — FR-J1939-006.
                // After a successful claim RebindTransportOnLoop re-opens _transport on the
                // claimed address, so it is safe to use directly for both TX (peer sees the
                // correct SA on RTS/DT) and RX (CTS/EOM are addressed back to this channel
                // identity).
                // Note (Copilot 3600424623): message.Priority is ignored on this path — TP.CM
                // / TP.DT use the channel's J1939TpOptions.Priority (default 7) because the
                // current IJ1939TpChannel API does not expose a per-send priority.
                // J1939Message.Priority documents the same. Callers who need a specific TP
                // priority must configure J1939NodeOptions.TransportOptions.Priority when
                // opening the node.
                var tpChannel = _transport;
                if (tpChannel.SourceAddress != sa)
                {
                    // Rebind hasn't landed yet (would be a claim/send race on the actor loop)
                    // — fall back to a single-use per-send TP channel bound to the currently-
                    // claimed SA so the wire carries the right SA regardless.
                    tpChannel = J1939Tp.J1939Tp.Open(_service, sourceAddress: sa,
                        options: _options.TransportOptions, leaveOpen: true);
                }
                try
                {
                    var payloadArr = message.Payload.ToArray();
                    if (message.DestinationAddress == J1939Pgn.GlobalAddress)
                        await tpChannel.SendBamAsync(message.Pgn, payloadArr, cancellationToken).ConfigureAwait(false);
                    else
                        await tpChannel.SendCmAsync(message.Pgn, message.DestinationAddress, payloadArr, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (!ReferenceEquals(tpChannel, _transport))
                        tpChannel.Dispose();
                }
            }
        }
        catch (ObjectDisposedException) when (HasReclaimCrossed(sa))
        {
            // Bugbot 3600591980: RebindTransportOnLoop disposed the shared TP channel while
            // our multi-frame send was awaiting SendBamAsync / SendCmAsync. That surfaces as
            // ObjectDisposedException from the TP channel; substitute the canonical
            // reclaim-failure so the caller sees the same failure mode as the pre-send gate
            // rather than an internal-implementation exception.
            throw new J1939NoAddressException();
        }

        // Bugbot 3600591980: in-flight sends MUST honor a concurrent reclaim. SendCoreAsync
        // captured the claim state / SA before awaiting the wire I/O, but ClaimAddressAsync
        // running on the actor loop between the initial gate and this point may have cleared
        // or moved the address (BeginClaim invalidates the address before announcing the new
        // preferred SA). If that happened the frame(s) we just placed on the wire went out
        // on the previous SA (single-frame path) or spanned the reclaim boundary (multi-
        // frame path); the caller must not observe a successful send that crossed reclaim.
        if (HasReclaimCrossed(sa))
            throw new J1939NoAddressException();
    }

    private bool HasReclaimCrossed(byte capturedSa)
        => (J1939ClaimState)Volatile.Read(ref _claimStateStore) != J1939ClaimState.Claimed
           || Volatile.Read(ref _addressStore) != capturedSa;

    /// <inheritdoc />
    public Task RequestPgnAsync(uint requestedPgn, byte destinationAddress = 0xFF,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (requestedPgn > J1939Pgn.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(requestedPgn), requestedPgn, "PGN must fit in 18 bits.");

        // Request-PGN payload is a 3-byte little-endian PGN.
        var payload = new byte[3];
        payload[0] = (byte)(requestedPgn & 0xFF);
        payload[1] = (byte)((requestedPgn >> 8) & 0xFF);
        payload[2] = (byte)((requestedPgn >> 16) & 0xFF);

        // Request PGN is 0xEA00 (PDU1). The Request frame itself is priority 6 on the wire
        // (SAE J1939-21 §5.3.2 for Request PGN).
        var message = new J1939Message(J1939Pgn.Request, payload, _options.ClaimPriority,
            sourceAddress: 0, destinationAddress: destinationAddress);
        return SendCoreAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    public IDisposable StartPeriodicSend(J1939Message message, TimeSpan period)
    {
        ThrowIfDisposed();
        if (period <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be positive.");
        if (message.Priority > 7)
            throw new ArgumentOutOfRangeException(nameof(message.Priority), message.Priority,
                "J1939 priority must be in [0, 7].");

        // Every periodic PGN — single-frame and multi-frame alike — is driven by the actor /
        // SendAsync loop in PeriodicSchedule. The earlier attempt to route single-frame
        // payloads through the L1 IPeriodicTx handle was reverted (PR #33): the SW-fallback
        // path swallowed Transmit exceptions (Bugbot 3604386825), and every attempted work-
        // around (reclaim-time handle rebind, address-loss teardown on the actor loop,
        // payload snapshot, handle-detach on dispose, …) revealed yet another edge case.
        // Collapsing to one path removes an entire class of races at the cost of the L1
        // jitter optimization — FR-J1939-007 (SRS Should) still holds via the L2 actor /
        // DeadlineScheduler path, which is what IJ1939Node.StartPeriodicSend documents.
        //
        // Pre-flight claim gate mirrors SendAsync (Bugbot 3600377725): refuse to arm a
        // schedule without a currently-claimed SA so callers see the same failure mode as
        // a one-shot SendAsync before ClaimAddressAsync completes. Once running, the loop's
        // per-emission SendAsync call re-checks the claim state on every tick, so an
        // address loss after Start stops wire traffic (SendAsync throws
        // J1939NoAddressException, which surfaces via BackgroundExceptionOccurred).
        if ((J1939ClaimState)Volatile.Read(ref _claimStateStore) != J1939ClaimState.Claimed)
            throw new J1939NoAddressException();
        if (Volatile.Read(ref _addressStore) < 0)
            throw new J1939NoAddressException();

        var schedule = new PeriodicSchedule(this, message, period);
        try
        {
            schedule.Start();
        }
        catch
        {
            schedule.Dispose();
            throw;
        }
        return schedule;
    }

    // =========================================================================================
    // Wire helpers
    // =========================================================================================

    private void TransmitFrame(uint canId, byte[] payload)
    {
        // Fire-and-forget: address-claim traffic doesn't need a task, but we still want a
        // background exception if the driver rejects it. SendConfirmed is used consistently
        // with the rest of the CanKit.Pro stack.
        _ = Task.Run(async () =>
        {
            try
            {
                using var frame = CanFrame.Classic(unchecked((int)canId), payload, isExtendedFrame: true);
                var confirmation = await _service.SendConfirmed(frame).ConfigureAwait(false);
                if (!confirmation.Confirmed)
                    RaiseBackgroundException(new J1939NodeException(
                        $"J1939 frame TX failed (id=0x{canId:X8}): {confirmation.FailureReason}."));
            }
            catch (Exception ex)
            {
                RaiseBackgroundException(ex);
            }
        });
    }

    // =========================================================================================
    // Reader loops
    // =========================================================================================

    private async Task RunReaderAsync()
    {
        try
        {
            await foreach (var frame in _subscription.Frames.WithCancellation(_readerCts.Token)
                .ConfigureAwait(false))
            {
                if (!frame.IsExtendedFrame) continue;
                var fields = J1939Id.Decompose((uint)frame.ID);
                // Skip TP traffic — the shared J1939-TP channel demuxes those separately.
                if (J1939Pgn.IsTransportCm(fields.Pgn) || J1939Pgn.IsTransportDt(fields.Pgn))
                    continue;
                var pgn = fields.Pgn;
                var sa = fields.SourceAddress;
                var da = fields.PduSpecific;
                var priority = fields.Priority;
                var payload = frame.Data.ToArray();
                var isPdu1 = fields.IsPdu1;
                _actor.Post(() => HandleIncomingFrame(pgn, priority, sa, da, isPdu1, payload));
            }
        }
        catch (OperationCanceledException) { /* expected on Dispose */ }
        catch (Exception ex) { RaiseBackgroundException(ex); }
    }

    private Task StartTransportReader(IJ1939TpChannel transport)
        => Task.Run(() => RunTransportReaderAsync(transport));

    // Bound to a specific transport instance so a rebind (which replaces _transport) does not
    // accidentally cause an already-running reader to switch enumerables mid-flight. The
    // per-instance inbox completes on channel Dispose, so this loop exits cleanly on rebind.
    private async Task RunTransportReaderAsync(IJ1939TpChannel transport)
    {
        try
        {
            await foreach (var datagram in transport.ReceiveAllAsync(_readerCts.Token).ConfigureAwait(false))
            {
                // Drop datagrams from a channel that has already been replaced by rebind
                // (we no longer Wait the old reader on the actor — Bugbot 3600717311).
                if (!ReferenceEquals(transport, _transport))
                    return;

                // Reassembled PDU: emit as a J1939Message just like a single-frame arrival.
                // `IJ1939Node.MessageReceived` documents that handlers run on the node's actor
                // loop; the transport reader is a separate Task, so we must marshal onto the
                // actor before firing the event so single-frame and multi-frame receive paths
                // share the same thread affinity guarantee (Bugbot 3600440957).
                var message = new J1939Message(datagram.Pgn, datagram.Payload,
                    priority: _options.DefaultPriority,
                    sourceAddress: datagram.SourceAddress,
                    destinationAddress: datagram.DestinationAddress);
                try
                {
                    _actor.Post(() =>
                    {
                        if (!ReferenceEquals(transport, _transport)) return;
                        EmitMessage(message);
                    });
                }
                catch (ObjectDisposedException)
                {
                    // Node was disposed while we had a datagram in hand; drop it silently —
                    // consistent with the RunReaderAsync path which also stops posting after
                    // dispose.
                    return;
                }
            }
        }
        catch (OperationCanceledException) { /* expected on Dispose */ }
        catch (ObjectDisposedException) { /* expected on rebind: old channel was disposed */ }
        catch (Exception ex) { RaiseBackgroundException(ex); }
    }

    /// <summary>
    /// Re-open <see cref="_transport"/> on <paramref name="sourceAddress"/>. Must run on the
    /// actor loop (single-writer discipline for <c>_transport</c> / <c>_transportReaderTask</c>).
    /// A no-op if the current channel already carries the requested SA.
    /// </summary>
    /// <remarks>
    /// Bugbot 3600591973: the previous channel MUST be disposed synchronously on the actor
    /// loop before a new channel is opened. Fire-and-forgetting <c>Dispose</c> in parallel
    /// with opening a fresh channel leaves both TP channels subscribed to the bus at the
    /// same time; broadcast TP.BAM (DA = 0xFF) is accepted by both channels and both fire
    /// reassembled datagrams, so <see cref="MessageReceived"/> sees each multi-frame BAM
    /// once per surviving channel. Dispose itself only cancels the bus subscription (fast);
    /// we deliberately do not <c>Wait</c> the old reader on the actor (Bugbot 3600717311) —
    /// stale posts are filtered by channel identity instead.
    /// If <c>J1939Tp.Open</c> then fails, the exception is surfaced via
    /// <see cref="BackgroundExceptionOccurred"/> — <c>_transport</c> continues to point at
    /// the now-disposed old channel so subsequent TP sends fail loudly rather than
    /// silently transmit on a stale SA.
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> when the node has a live TP channel bound to
    /// <paramref name="sourceAddress"/> after this call; <see langword="false"/> when open
    /// failed after disposing the previous channel (multi-frame path broken).
    /// </returns>
    private bool RebindTransportOnLoop(byte sourceAddress)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        var current = _transport;
        if (current is not null && current.SourceAddress == sourceAddress) return true;

        // Tear down the previous transport BEFORE opening the new one so no window exists
        // where two subscribed channels can both surface the same broadcast TP.BAM
        // (Bugbot 3600591973). Disposing the channel cancels its bus subscription
        // immediately and completes its inbox. Do NOT Wait on the reader here — that would
        // stall the single ProtocolActor for up to ~2 s and block address-claim handling
        // (Bugbot 3600717311). Late datagrams from the old reader are dropped via the
        // ReferenceEquals(transport, _transport) gate in RunTransportReaderAsync.
        if (current is not null)
        {
            current.BackgroundExceptionOccurred -= OnTransportBackgroundException;
            try { current.Dispose(); }
            catch (Exception ex) { RaiseBackgroundException(ex); }
        }

        IJ1939TpChannel newTransport;
        try
        {
            newTransport = J1939Tp.J1939Tp.Open(_service, sourceAddress: sourceAddress,
                options: _options.TransportOptions, leaveOpen: true);
        }
        catch (Exception ex)
        {
            // The old channel is already disposed; the node's multi-frame path is broken
            // until the next successful rebind, but single-frame traffic keeps working via
            // the direct service. Surface so the application can react. Callers that must
            // not advertise Claimed without a live TP (claim commit) check the return value
            // (Bugbot 3600825931).
            RaiseBackgroundException(ex);
            return false;
        }

        newTransport.BackgroundExceptionOccurred += OnTransportBackgroundException;
        _transport = newTransport;
        _transportReaderTask = StartTransportReader(newTransport);
        return true;
    }

    private void HandleIncomingFrame(uint pgn, byte priority, byte sa, byte da, bool isPdu1, byte[] payload)
    {
        try
        {
            // Address Claim (PGN 0xEE00): drive the state machine and stop; not an application PGN.
            if (J1939Pgn.IsAddressClaim(pgn))
            {
                HandleIncomingAddressClaim(sa, payload);
                return;
            }

            // Only surface application PGNs that are either broadcast (PDU2) or directed at us.
            int myAddr = Volatile.Read(ref _addressStore);
            if (isPdu1 && da != J1939Pgn.GlobalAddress && (myAddr < 0 || da != (byte)myAddr))
                return;

            var message = new J1939Message(pgn, payload, priority, sa,
                isPdu1 ? da : J1939Pgn.GlobalAddress);
            EmitMessage(message);
        }
        catch (Exception ex)
        {
            RaiseBackgroundException(ex);
        }
    }

    private void EmitMessage(J1939Message message)
    {
        try
        {
            MessageReceived?.Invoke(this, message);
        }
        catch (Exception ex)
        {
            RaiseBackgroundException(ex);
        }
        _rxInbox.Writer.TryWrite(message);
    }

    // =========================================================================================
    // Dispose
    // =========================================================================================

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try { _readerCts.Cancel(); } catch { }
        _rxInbox.Writer.TryComplete();

        try
        {
            _actor.Post(() =>
            {
                var pending = _pendingClaim;
                if (pending is not null)
                {
                    _pendingClaim = null;
                    pending.Deadline?.Dispose();
                    pending.CtRegistration.Dispose();
                    pending.Tcs.TrySetException(new ObjectDisposedException(nameof(J1939NodeImpl)));
                }
            });
        }
        catch (ObjectDisposedException) { /* actor already gone */ }

        try { _readerTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _transportReaderTask.Wait(TimeSpan.FromSeconds(2)); } catch { }

        _subscription.Dispose();
        _transport.Dispose();
        _actor.Dispose();
        _readerCts.Dispose();
        if (_ownsService) _service.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    // =========================================================================================
    // Diagnostics helpers
    // =========================================================================================

    internal IAsyncEnumerable<J1939Message> InboxAll(CancellationToken cancellationToken)
        => _rxInbox.Reader.ReadAllAsync(cancellationToken);

    private void RaiseBackgroundException(Exception ex)
    {
        try { BackgroundExceptionOccurred?.Invoke(this, ex); }
        catch { /* misbehaving subscriber must not tear the node down */ }
    }

    private void OnActorBackgroundException(object? sender, Exception ex) => RaiseBackgroundException(ex);
    private void OnTransportBackgroundException(object? sender, Exception ex) => RaiseBackgroundException(ex);

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(J1939NodeImpl));
    }

    // =========================================================================================
    // Nested types
    // =========================================================================================

    // Boxed state passed to CancellationToken.Register so the callback can post the actor
    // teardown for the specific in-flight claim it corresponds to.
    private sealed class ClaimCancelState
    {
        public ClaimCancelState(J1939NodeImpl node, TaskCompletionSource<object?> tcs)
        {
            Node = node;
            Tcs = tcs;
        }

        public J1939NodeImpl Node { get; }
        public TaskCompletionSource<object?> Tcs { get; }
    }

    private sealed class PendingClaim
    {
        public PendingClaim(byte preferredAddress, TaskCompletionSource<object?> tcs, IDeadline? deadline,
            CancellationTokenRegistration ctRegistration)
        {
            PreferredAddress = preferredAddress;
            Tcs = tcs;
            Deadline = deadline;
            CtRegistration = ctRegistration;
        }

        public byte PreferredAddress { get; }
        public TaskCompletionSource<object?> Tcs { get; }
        public IDeadline? Deadline { get; set; }
        public CancellationTokenRegistration CtRegistration { get; }
    }

    /// <summary>
    /// Software periodic-send schedule used for every periodic PGN — single-frame and
    /// multi-frame alike (FR-J1939-006/007). Each iteration calls
    /// <see cref="J1939NodeImpl.SendAsync"/>, which re-checks the claim gate before
    /// touching the wire (so address loss stops emission automatically) and, for
    /// multi-frame PGNs, spins up a fresh TP.BAM / TP.CM session on the shared J1939-TP
    /// channel. Send failures do not tear the schedule down; they surface via
    /// <see cref="J1939NodeImpl.BackgroundExceptionOccurred"/>. The payload is snapshotted
    /// into an owned buffer at construction so in-place caller mutation after Start is not
    /// observable on the wire (Bugbot 3604566680).
    /// </summary>
    private sealed class PeriodicSchedule : IDisposable
    {
        private readonly J1939NodeImpl _owner;
        private readonly J1939Message _message;
        private readonly TimeSpan _period;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;
        private int _disposed;

        public PeriodicSchedule(J1939NodeImpl owner, J1939Message message, TimeSpan period)
        {
            _owner = owner;
            _period = period;

            // Snapshot the caller's payload into an owned array so the wire traffic is
            // frozen at Start-time regardless of whether the caller mutates the buffer that
            // backs message.Payload afterwards (Bugbot 3604566680). J1939Message.Payload is
            // a ReadOnlyMemory<byte> and its ctor doesn't copy, so re-reading it every
            // emission would alias the caller's buffer — J1939Message's own contract is
            // "payload is copied by the sender". We rebuild the message once here with the
            // owned array so every LoopAsync iteration hands SendAsync the same immutable
            // bytes.
            var owned = new byte[message.Payload.Length];
            if (message.Payload.Length > 0) message.Payload.Span.CopyTo(owned);
            _message = new J1939Message(message.Pgn, owned, message.Priority,
                sourceAddress: message.SourceAddress,
                destinationAddress: message.DestinationAddress);
        }

        public void Start()
        {
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await _owner.SendAsync(_message, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (ObjectDisposedException) { return; }
                    catch (Exception ex)
                    {
                        _owner.RaiseBackgroundException(ex);
                    }

                    try
                    {
                        await Task.Delay(_period, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { return; }
                }
            }
            catch { /* observed via BackgroundExceptionOccurred */ }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _cts.Cancel(); } catch { }
            try { _loop?.GetAwaiter().GetResult(); } catch { }
            _cts.Dispose();
        }
    }

    // Historical note: an earlier revision routed single-frame (<= 8 byte) periodic PGNs
    // through the L1 ICanBus.TransmitPeriodic / IPeriodicTx handle for lower jitter. That
    // dual-path design was reverted (PR #33) because the SW-fallback branch swallowed
    // Transmit exceptions and every attempted work-around opened a new race (reclaim-time
    // rebind, dispose-of-detached-handle, payload aliasing, actor stall, claim gate). All
    // periodic PGNs currently flow through PeriodicSchedule (SendAsync + Task.Delay).
    // FR-J1939-007 (Should) is still satisfied via L2 actor / DeadlineScheduler timing.
    //
    // The specific L1 blocker — SoftwarePeriodicTx.TrySendOnce silently swallowing every
    // Transmit exception — has now been removed: IPeriodicTx exposes a Faulted event
    // (EventHandler<Exception>) that SoftwarePeriodicTx raises outside its internal gate
    // whenever the inner Transmit call throws, and the loop stays alive so transient
    // failures no longer terminate a schedule invisibly. Wiring J1939 to subscribe to
    // Faulted and forward exceptions through BackgroundExceptionOccurred (mirroring the
    // current actor-loop path) can be done as a follow-up when the native optimization
    // is reintroduced; the L1 error-propagation prerequisite itself is resolved.
}
