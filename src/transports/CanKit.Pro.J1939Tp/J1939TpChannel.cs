using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Pro.Actor;
using CanKit.Pro.Addressing;
using CanKit.Pro.RawCan;
using CanKit.Pro.Reliability;

namespace CanKit.Pro.J1939Tp;

/// <summary>
/// Actor-driven <see cref="IJ1939TpChannel"/> that composes on top of the CanKit.Pro L2
/// services: <see cref="ICanBusService"/> for RX demux and TX confirmation,
/// <see cref="IProtocolActor"/> for single-writer per-session state, and
/// <see cref="DeadlineScheduler"/> for the J1939-21 §5.10.2.4 timers T1..T4/Tr/Th (SRS
/// FR-TP-032/034).
///
/// <para>
/// Session keying follows what the wire can actually distinguish, not just what an initiator
/// might wish for. J1939-21 §5.10.3 explicitly restricts a receiver to a single active TP.CM
/// connection per (source, destination) pair and treats BAM as a per-source broadcast: a
/// second concurrent connection of the same kind from the same peer is a protocol error to be
/// refused (RTS → Abort, code 1/7), and a new BAM implicitly supersedes any previous BAM from
/// the same source per §5.10.3 ("new BAM aborts previous"). TP.DT frames carry no PGN, so we
/// literally cannot demultiplex two overlapping RX sessions from one source even if we wanted
/// to -- the DT frame's only routing keys are (SA, DA), and DA==0xFF vs SA distinguishes
/// broadcast (BAM) from directed (CM). We therefore key <see cref="_rxSessions"/> on
/// <c>(source address, kind)</c>: at most one BAM and one CM RX session per peer at any time.
/// </para>
/// <para>
/// TX sessions are keyed by <c>(destination address, PGN)</c>: we are the initiator, so we
/// only allow one outbound TP.CM per (dst, PGN) but do not restrict *ourselves* from opening
/// two TP.CM sessions to two different destinations -- the peer at each destination sees only
/// one connection from us and remains within its own §5.10.3 quota. Any number of concurrent
/// TX sessions with distinct (dst, PGN) tuples therefore runs in parallel without interfering
/// (FR-TP-034/035).
/// </para>
/// </summary>
internal sealed class J1939TpChannel : IJ1939TpChannel
{
    private readonly ICanBusService _service;
    private readonly bool _ownsService;
    private readonly byte _sourceAddress;
    private readonly J1939TpOptions _options;

    private readonly ProtocolActor _actor;
    private readonly DeadlineScheduler _deadlines;
    private readonly ISubscription _subscription;
    private readonly Task _readerTask;
    private readonly CancellationTokenSource _readerCts = new();

    // Bounded PDU inbox (one entry per fully reassembled datagram). Written only from the actor
    // loop; consumers may read concurrently. Drop-oldest so a slow reader never stalls the RX
    // state machine (mirrors the L2 subscription policy).
    private readonly Channel<J1939TpDatagram> _pduInbox;

    // Per-session state -- keyed differently for TX and RX because the two directions have
    // different identifiers on the wire (TX is per-destination, RX is per-source). Both maps are
    // only ever touched from the actor loop, so no lock is needed.
    private readonly Dictionary<TxSessionKey, TxSession> _txSessions = new();
    private readonly Dictionary<RxSessionKey, RxSession> _rxSessions = new();

    private int _disposed;

    /// <inheritdoc />
    public byte SourceAddress => _sourceAddress;

    /// <inheritdoc />
    public J1939TpOptions Options => _options;

    /// <inheritdoc />
    public event EventHandler<J1939TpDatagram>? DatagramReceived;

    /// <inheritdoc />
    public event EventHandler<Exception>? BackgroundExceptionOccurred;

    internal J1939TpChannel(ICanBusService service, byte sourceAddress, J1939TpOptions options, bool ownsService)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        if (sourceAddress == J1939Pgn.GlobalAddress)
            throw new ArgumentOutOfRangeException(nameof(sourceAddress),
                "J1939 source address 0xFF (global) is not a valid channel identity.");
        _sourceAddress = sourceAddress;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _ownsService = ownsService;

        var inboxOptions = new BoundedChannelOptions(Math.Max(1, _options.ReceiveBufferCapacity))
        {
            SingleReader = false,
            SingleWriter = true, // written only from the actor loop
            FullMode = BoundedChannelFullMode.DropOldest,
        };
        _pduInbox = Channel.CreateBounded<J1939TpDatagram>(inboxOptions);

        _actor = new ProtocolActor();
        _actor.BackgroundExceptionOccurred += OnActorBackgroundException;
        _deadlines = new DeadlineScheduler(_actor);

        try
        {
            // Subscribe to every 29-bit ID whose PF byte identifies TP.CM (0xEC) or TP.DT (0xEB)
            // -- both PDU1 formats, so PS is the destination address; we filter by destination
            // (global 0xFF or our own SA) at the actor. Two mask filters cover both PFs; anything
            // else on the bus is skipped by the demux without ever reaching us.
            var tpCmFilter = CanIdFilter.Mask(
                accCode: (uint)J1939Pgn.TpCm << 8,      // PF = 0xEC in bits 23..16 of the 29-bit ID
                accMask: 0x00FF0000u,
                idType: CanFilterIDType.Extend);
            var tpDtFilter = CanIdFilter.Mask(
                accCode: (uint)J1939Pgn.TpDt << 8,      // PF = 0xEB in bits 23..16
                accMask: 0x00FF0000u,
                idType: CanFilterIDType.Extend);
            _subscription = _service.Subscribe(f => tpCmFilter.Matches(f) || tpDtFilter.Matches(f));
        }
        catch
        {
            _actor.Dispose();
            throw;
        }

        _readerTask = Task.Run(RunReaderAsync);
    }

    /// <inheritdoc />
    public Task SendBamAsync(uint pgn, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateSendPayload(pgn, payload.Length);

        var pduBytes = payload.ToArray();
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = new TxSessionKey(J1939Pgn.GlobalAddress, pgn);

        RegisterCancellation(tcs, cancellationToken, key);
        _actor.Post(() => BeginTxOnLoop(key, pduBytes, tcs, isCm: false));
        return tcs.Task;
    }

    /// <inheritdoc />
    public Task SendCmAsync(uint pgn, byte destinationAddress, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (destinationAddress == J1939Pgn.GlobalAddress)
            throw new ArgumentOutOfRangeException(nameof(destinationAddress),
                "TP.CM requires a specific destination address; use SendBamAsync for broadcasts.");
        ValidateSendPayload(pgn, payload.Length);

        var pduBytes = payload.ToArray();
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = new TxSessionKey(destinationAddress, pgn);

        RegisterCancellation(tcs, cancellationToken, key);
        _actor.Post(() => BeginTxOnLoop(key, pduBytes, tcs, isCm: true));
        return tcs.Task;
    }

    private static void ValidateSendPayload(uint pgn, int payloadLength)
    {
        if (pgn > J1939Pgn.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(pgn), pgn, "PGN must fit in 18 bits.");
        if (payloadLength < J1939TpFrames.MinTpPayloadLength || payloadLength > J1939TpFrames.MaxTpPayloadLength)
            throw new ArgumentOutOfRangeException(nameof(payloadLength), payloadLength,
                $"J1939-TP payload length must be in [{J1939TpFrames.MinTpPayloadLength}, {J1939TpFrames.MaxTpPayloadLength}] bytes.");
    }

    private void RegisterCancellation(TaskCompletionSource<object?> tcs, CancellationToken ct, TxSessionKey key)
    {
        if (!ct.CanBeCanceled) return;
        // Hop cancellation onto the actor so we clean up the session state under the single-writer
        // discipline (rather than racing the actor from whatever thread cancels the token).
        ct.Register(static state =>
        {
            var (self, k, t, token) = ((J1939TpChannel, TxSessionKey, TaskCompletionSource<object?>, CancellationToken))state!;
            try
            {
                self._actor.Post(() => self.CancelTxOnLoop(k, t, token));
            }
            catch (ObjectDisposedException)
            {
                t.TrySetCanceled(token);
            }
        }, (this, key, tcs, ct));
    }

    /// <summary>
    /// Actor-loop handler for a caller canceling an in-flight (or not-yet-started) TX. If a CM
    /// session was already opened with the peer (RTS / DTs on the wire), emit a TP.CM Connection
    /// Abort so the peer does not keep waiting on T1/T2/T3. BAM has no abort channel.
    /// </summary>
    private void CancelTxOnLoop(TxSessionKey key, TaskCompletionSource<object?> tcs, CancellationToken token)
    {
        if (_txSessions.TryGetValue(key, out var session) && ReferenceEquals(session.Tcs, tcs))
        {
            _txSessions.Remove(key);
            session.Deadline?.Dispose();
            session.Deadline = null;
            if (session.IsCm)
            {
                // Peer already has (or is about to have) an open CM session with us — tell it to
                // tear down. Reason 2 ("resources needed for another task") is the closest
                // standard code for an application-initiated local cancel.
                SendTpCm(J1939TpFrames.BuildAbort(J1939TpAbortReason.NoResourcesAvailable, key.Pgn),
                    destinationAddress: key.DestinationAddress);
            }
        }
        // Complete with the caller's token (do not go through TxSession.Cancel, which would
        // TrySetCanceled() without the token and win the race against this call).
        tcs.TrySetCanceled(token);
    }

    /// <inheritdoc />
    public async Task<J1939TpDatagram> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (await _pduInbox.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_pduInbox.Reader.TryRead(out var pdu))
                return pdu;
        }
        throw new InvalidOperationException("Channel is disposed; no more datagrams will arrive.");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<J1939TpDatagram> ReceiveAllAsync(CancellationToken cancellationToken = default)
        => ReadAllAsync(cancellationToken);

    private async IAsyncEnumerable<J1939TpDatagram> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = _pduInbox.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var pdu))
                yield return pdu;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try { _readerCts.Cancel(); } catch { /* nothing else to do */ }

        // Complete the inbox first so consumers awaiting ReceiveAsync unblock before we tear
        // down the reader task.
        _pduInbox.Writer.TryComplete();

        // Cancel every still-in-flight session on the actor so their TCSs get an
        // ObjectDisposedException instead of hanging on the now-disposed inbox.
        try
        {
            _actor.Post(() =>
            {
                foreach (var kv in _txSessions)
                    kv.Value.Fail(new ObjectDisposedException(nameof(J1939TpChannel)));
                _txSessions.Clear();
                foreach (var kv in _rxSessions)
                    kv.Value.Cancel();
                _rxSessions.Clear();
            });
        }
        catch (ObjectDisposedException)
        {
            // actor already gone; nothing more to do
        }

        try { _readerTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* observed via task; not fatal */ }

        _subscription.Dispose();
        _actor.Dispose();
        _readerCts.Dispose();

        if (_ownsService)
            _service.Dispose();
    }

    // -----------------------------------------------------------------------------------------
    // Subscription reader -- one long-lived Task per channel that pushes RX frames onto the actor.
    // -----------------------------------------------------------------------------------------
    private async Task RunReaderAsync()
    {
        try
        {
            await foreach (var frame in _subscription.Frames.WithCancellation(_readerCts.Token)
                .ConfigureAwait(false))
            {
                if (!frame.IsExtendedFrame) continue; // J1939-TP is 29-bit only
                var payload = frame.Data.ToArray();
                if (payload.Length < 8) continue; // TP.CM / TP.DT are always 8 bytes on the wire

                var fields = J1939Id.Decompose((uint)frame.ID);
                // TP.CM (0xEC) and TP.DT (0xEB) are PDU1 PFs, so PduSpecific is the destination
                // address (never a group extension).
                byte destination = fields.PduSpecific;
                // Destination filter: BAM/CM directed at us (SA) or globally broadcast (0xFF).
                if (destination != _sourceAddress && destination != J1939Pgn.GlobalAddress)
                    continue;
                // Also skip anything we sent ourselves (a bus in Echo mode replays TX frames --
                // handling our own SA as if a foreign peer sent it would spuriously open a
                // session against ourselves).
                if (fields.SourceAddress == _sourceAddress)
                    continue;

                var pgn = fields.Pgn;
                var sa = fields.SourceAddress;
                var da = destination;
                _actor.Post(() => HandleIncoming(pgn, sa, da, payload));
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

    private void HandleIncoming(uint pgn, byte sa, byte da, byte[] payload)
    {
        try
        {
            if (J1939Pgn.IsTransportCm(pgn))
                HandleRxTpCm(sa, da, payload);
            else if (J1939Pgn.IsTransportDt(pgn))
                HandleRxTpDt(sa, da, payload);
        }
        catch (Exception ex)
        {
            RaiseBackgroundException(ex);
        }
    }

    // =========================================================================================
    // RX side
    // =========================================================================================
    private void HandleRxTpCm(byte sa, byte da, byte[] payload)
    {
        byte control = payload[0];
        uint dataPgn = J1939TpFrames.ReadDataPgn(payload);

        switch (control)
        {
            case J1939TpFrames.ControlBam:
                {
                    // §5.10.3: at most one BAM per source at any time. A fresh BAM from the same
                    // sender implicitly supersedes any previous (possibly half-built) one --
                    // there is no ack channel to reject the new one on.
                    var bamKey = new RxSessionKey(sa, J1939TpKind.Bam);
                    if (_rxSessions.TryGetValue(bamKey, out var existing))
                    {
                        existing.Cancel();
                        _rxSessions.Remove(bamKey);
                    }
                    int totalBytes = payload[1] | (payload[2] << 8);
                    int totalPackets = payload[3];
                    if (!IsValidTotals(totalBytes, totalPackets))
                    {
                        // Malformed BAM: silently drop -- broadcast has no ack channel to report on.
                        return;
                    }
                    var session = RxSession.NewBam(sa, dataPgn, totalBytes, totalPackets, _deadlines, _options, OnRxT1Expired);
                    _rxSessions[bamKey] = session;
                    break;
                }

            case J1939TpFrames.ControlRts:
                {
                    // §5.10.3: only one CM connection per (SA, DA) pair, so if a CM session with
                    // this source is already in progress -- even for a *different* PGN -- refuse
                    // the new one and leave the ongoing one intact. Emit the abort against the
                    // new RTS's PGN so the peer can correlate the rejection.
                    var cmKey = new RxSessionKey(sa, J1939TpKind.Cm);
                    if (_rxSessions.ContainsKey(cmKey))
                    {
                        SendTpCm(J1939TpFrames.BuildAbort(J1939TpAbortReason.SessionAlreadyOpen, dataPgn),
                            destinationAddress: sa);
                        return;
                    }

                    int totalBytes = payload[1] | (payload[2] << 8);
                    int totalPackets = payload[3];
                    byte maxCts = payload[4];
                    if (!IsValidTotals(totalBytes, totalPackets))
                    {
                        SendTpCm(J1939TpFrames.BuildAbort(J1939TpAbortReason.Unknown, dataPgn),
                            destinationAddress: sa);
                        return;
                    }

                    // Send CTS for the first block, capping at our advertised max-packets-per-CTS
                    // and at the peer's own RTS cap (0xFF = "no limit" per §5.10.3.1).
                    byte cap = _options.MaxPacketsPerCts;
                    if (maxCts != 0xFF && maxCts > 0 && maxCts < cap) cap = maxCts;
                    byte block = (byte)Math.Min(cap, totalPackets);
                    var session = RxSession.NewCm(sa, dataPgn, totalBytes, totalPackets, cap,
                        _deadlines, _options, OnRxT1Expired, OnRxTrExpired);
                    session.NextExpectedSn = 1;
                    session.BlockRemaining = block;
                    _rxSessions[cmKey] = session;
                    SendTpCm(J1939TpFrames.BuildCts(block, 1, dataPgn), destinationAddress: sa);
                    // Tr covers the gap from CTS → first DT of the block (FR-TP-032 / §5.10.2.4).
                    session.ArmTr();
                    break;
                }

            case J1939TpFrames.ControlAbort:
                {
                    // Aborts are per-CM-session; only cancel the session if the peer aborted the
                    // PGN we're actually reassembling. A stray abort for a different PGN (e.g.
                    // late-arriving frame from a previous, already-torn-down session) must not
                    // kill the current transfer.
                    var cmKey = new RxSessionKey(sa, J1939TpKind.Cm);
                    if (_rxSessions.TryGetValue(cmKey, out var existing) && existing.Pgn == dataPgn)
                    {
                        existing.Cancel();
                        _rxSessions.Remove(cmKey);
                        RaiseBackgroundException(new J1939TpAbortException(
                            (J1939TpAbortReason)payload[1], dataPgn,
                            $"Peer 0x{sa:X2} aborted TP.CM RX session for PGN 0x{dataPgn:X}."));
                    }
                    break;
                }

            case J1939TpFrames.ControlCts:
            case J1939TpFrames.ControlEomAck:
                {
                    // These are TX-side responses -- handled by the TX session for (peer=sa, pgn).
                    HandleRxTxSideResponse(sa, dataPgn, payload);
                    break;
                }
        }
    }

    private void HandleRxTpDt(byte sa, byte da, byte[] payload)
    {
        // TP.DT carries no PGN of its own, so we route strictly by the two identifiers the DT
        // frame *does* carry: source address, and destination (broadcast 0xFF => BAM; our SA
        // => CM). Combined with J1939-21 §5.10.3's "one connection per (SA, DA) pair" rule,
        // this maps 1:1 to a single active RX session, so we can look it up in O(1) with no
        // ambiguity even when concurrent transfers of *different* PGNs from different peers
        // are in flight.
        var kind = da == J1939Pgn.GlobalAddress ? J1939TpKind.Bam : J1939TpKind.Cm;
        var matchKey = new RxSessionKey(sa, kind);
        if (!_rxSessions.TryGetValue(matchKey, out var match)) return;

        byte sn = payload[0];
        if (sn != match.NextExpectedSn)
        {
            // For BAM: silently drop the malformed stream and let T1 clean up.
            // For CM: abort with code 5.
            if (match.Kind == J1939TpKind.Cm)
            {
                SendTpCm(J1939TpFrames.BuildAbort(J1939TpAbortReason.UnexpectedCtsSequenceNumber, match.Pgn),
                    destinationAddress: sa);
                match.Cancel();
                _rxSessions.Remove(matchKey);
            }
            else
            {
                match.Cancel();
                _rxSessions.Remove(matchKey);
            }
            return;
        }

        int offset = (sn - 1) * J1939TpFrames.DtDataBytes;
        int remaining = match.Buffer.Length - offset;
        int copy = Math.Min(J1939TpFrames.DtDataBytes, remaining);
        Buffer.BlockCopy(payload, 1, match.Buffer, offset, copy);
        match.NextExpectedSn = (byte)(sn + 1);
        match.PacketsReceived++;
        // First DT of a CTS block clears Tr and switches to T1; subsequent DTs rearm T1.
        match.OnDtReceived();

        if (match.PacketsReceived >= match.TotalPackets)
        {
            // Complete session.
            if (match.Kind == J1939TpKind.Cm)
            {
                // Send EndOfMsgAck; the T3 deadline the peer arms on their side stops when it
                // arrives. §5.10.3.4.
                SendTpCm(J1939TpFrames.BuildEomAck(match.Buffer.Length, match.TotalPackets, match.Pgn),
                    destinationAddress: sa);
            }
            match.Cancel();
            _rxSessions.Remove(matchKey);
            EmitPdu(new J1939TpDatagram(match.Pgn, sa, match.Kind == J1939TpKind.Cm ? _sourceAddress : J1939Pgn.GlobalAddress,
                match.Kind, match.Buffer));
            return;
        }

        // Not the end -- but for CM, is this the end of the currently CTS'd block? If so, arm
        // the next CTS.
        if (match.Kind == J1939TpKind.Cm)
        {
            match.BlockRemaining--;
            if (match.BlockRemaining <= 0)
            {
                int totalRemaining = match.TotalPackets - match.PacketsReceived;
                byte cap = match.MaxPacketsPerCts;
                byte block = (byte)Math.Min(cap, totalRemaining);
                match.BlockRemaining = block;
                SendTpCm(J1939TpFrames.BuildCts(block, match.NextExpectedSn, match.Pgn),
                    destinationAddress: sa);
                // Fresh Tr for the next block's first DT (T1 only runs inside a block).
                match.ArmTr();
            }
        }
    }

    private void OnRxT1Expired(RxSession session)
        => ExpireRxSession(session, "T1", "waiting for next TP.DT");

    private void OnRxTrExpired(RxSession session)
        => ExpireRxSession(session, "Tr", "waiting for first TP.DT after CTS");

    private void ExpireRxSession(RxSession session, string timerName, string waitingFor)
    {
        // Look up in O(1) via the (peer, kind) key; guard against races where the session was
        // already removed (e.g. finished / aborted / superseded) before the timer fired.
        var key = new RxSessionKey(session.PeerAddress, session.Kind);
        if (!_rxSessions.TryGetValue(key, out var current) || !ReferenceEquals(current, session))
            return;
        _rxSessions.Remove(key);
        if (session.Kind == J1939TpKind.Cm)
        {
            SendTpCm(J1939TpFrames.BuildAbort(J1939TpAbortReason.Timeout, session.Pgn),
                destinationAddress: session.PeerAddress);
        }
        RaiseBackgroundException(new J1939TpAbortException(J1939TpAbortReason.Timeout, session.Pgn,
            $"J1939-TP {session.Kind} RX session timed out ({timerName}) {waitingFor} from 0x{session.PeerAddress:X2}."));
    }

    private static bool IsValidTotals(int totalBytes, int totalPackets)
        => totalBytes >= J1939TpFrames.MinTpPayloadLength
            && totalBytes <= J1939TpFrames.MaxTpPayloadLength
            && totalPackets >= 1
            && totalPackets <= 255
            && J1939TpFrames.TotalPackets(totalBytes) == totalPackets;

    // =========================================================================================
    // TX side
    // =========================================================================================
    private void BeginTxOnLoop(TxSessionKey key, byte[] pdu, TaskCompletionSource<object?> tcs, bool isCm)
    {
        if (_disposed != 0)
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(J1939TpChannel)));
            return;
        }
        // Cancel may have completed the TCS (via CancelTxOnLoop) before this work item ran —
        // do not register a session or emit TP.CM/TP.DT for an already-canceled send.
        if (tcs.Task.IsCompleted)
            return;
        if (_txSessions.ContainsKey(key))
        {
            tcs.TrySetException(new InvalidOperationException(
                $"A J1939-TP {(isCm ? "TP.CM" : "TP.BAM")} session for destination 0x{key.DestinationAddress:X2} PGN 0x{key.Pgn:X} is already in flight."));
            return;
        }

        int totalPackets = J1939TpFrames.TotalPackets(pdu.Length);
        var session = new TxSession(key, pdu, totalPackets, tcs, isCm);
        _txSessions[key] = session;

        if (isCm)
        {
            var rts = J1939TpFrames.BuildRts(pdu.Length, totalPackets, _options.MaxPacketsPerCts, key.Pgn);
            SendTpCm(rts, destinationAddress: key.DestinationAddress);
            // Now wait for CTS with T3 (initial), per §5.10.2.4.
            session.State = TxStage.WaitCts;
            session.Deadline = _deadlines.Arm(_options.T3, () => OnTxT3Expired(key));
        }
        else
        {
            var bam = J1939TpFrames.BuildBam(pdu.Length, totalPackets, key.Pgn);
            SendTpCm(bam, destinationAddress: J1939TpFrames.GlobalDestinationAddress);
            // BAM sender: hold-off Th between BAM and first DT, then Th between subsequent DTs.
            session.State = TxStage.SendingDt;
            session.NextSn = 1;
            _actor.Schedule(_options.Th, () => TrySendNextBamDt(key));
        }
    }

    private void HandleRxTxSideResponse(byte sa, uint dataPgn, byte[] payload)
    {
        var key = new TxSessionKey(sa, dataPgn);
        if (!_txSessions.TryGetValue(key, out var session))
            return; // stray CTS/EOM for a session we don't have -- drop.

        if (!session.IsCm) return; // BAM has no response frames

        byte control = payload[0];
        if (control == J1939TpFrames.ControlCts)
        {
            byte numPackets = payload[1];
            byte nextSn = payload[2];
            // §5.10.3.1: numPackets=0 with SN=0 is a "hold connection open" (wait). We arm T4
            // and stay in WaitCts.
            if (numPackets == 0)
            {
                session.Deadline?.Dispose();
                session.Deadline = _deadlines.Arm(_options.T4, () => OnTxT4Expired(key));
                return;
            }

            // Expected SN for the *next* block. While the last DT of the current block is still
            // awaiting SendConfirmed, NextSn has not yet advanced — but a fast peer (Virtual
            // loopback) may already have received that DT and emitted the next CTS. Accept a CTS
            // that asks for NextSn + BlockRemaining in that race, and apply it once the block drains.
            int expectedSn = session.State == TxStage.SendingDt && session.BlockRemaining > 0
                ? session.NextSn + session.BlockRemaining
                : (session.NextSn == 0 ? 1 : session.NextSn);
            if (nextSn != expectedSn)
            {
                AbortTx(session, J1939TpAbortReason.UnexpectedCtsSequenceNumber,
                    $"Peer requested SN {nextSn} but we expected SN {expectedSn}.");
                return;
            }

            int packetsBeforeNextBlock = session.State == TxStage.SendingDt
                ? Math.Max(0, session.NextSn + Math.Max(0, session.BlockRemaining) - 1)
                : Math.Max(0, (session.NextSn == 0 ? 1 : session.NextSn) - 1);
            int totalRemaining = session.TotalPackets - packetsBeforeNextBlock;
            if (numPackets > totalRemaining)
            {
                AbortTx(session, J1939TpAbortReason.UnexpectedCtsNumPackets,
                    $"Peer requested {numPackets} packets but only {totalRemaining} remain.");
                return;
            }

            // Early CTS while the current block is still draining: stash and apply on block end.
            if (session.State == TxStage.SendingDt && session.BlockRemaining > 0)
            {
                session.PendingCtsNumPackets = numPackets;
                session.PendingCtsNextSn = nextSn;
                session.HasPendingCts = true;
                session.Deadline?.Dispose();
                session.Deadline = null;
                return;
            }

            session.State = TxStage.SendingDt;
            session.NextSn = nextSn;
            session.BlockRemaining = numPackets;
            session.HasPendingCts = false;
            session.Deadline?.Dispose();
            session.Deadline = null;
            TrySendNextCmDt(key);
        }
        else if (control == J1939TpFrames.ControlEomAck)
        {
            // EOM matches our totals? If not, still complete -- but log via background exception
            // since the peer misinterpreted the payload.
            int totalBytes = payload[1] | (payload[2] << 8);
            int totalPackets = payload[3];
            _txSessions.Remove(key);
            session.Deadline?.Dispose();
            session.Deadline = null;
            if (totalBytes != session.Pdu.Length || totalPackets != session.TotalPackets)
            {
                RaiseBackgroundException(new J1939TpException(
                    $"Peer EOM ack size mismatch (expected {session.Pdu.Length}/{session.TotalPackets}, got {totalBytes}/{totalPackets})."));
            }
            session.Tcs.TrySetResult(null);
        }
        else if (control == J1939TpFrames.ControlAbort)
        {
            var reason = (J1939TpAbortReason)payload[1];
            _txSessions.Remove(key);
            session.Deadline?.Dispose();
            session.Deadline = null;
            session.Tcs.TrySetException(new J1939TpAbortException(reason, session.Key.Pgn,
                $"Peer 0x{sa:X2} aborted TP.CM session for PGN 0x{session.Key.Pgn:X}: {reason}."));
        }
    }

    private void TrySendNextBamDt(TxSessionKey key)
    {
        if (!_txSessions.TryGetValue(key, out var session) || session.IsCm) return;
        int offset = (session.NextSn - 1) * J1939TpFrames.DtDataBytes;
        byte sn = session.NextSn;
        var dt = J1939TpFrames.BuildDt(sn, session.Pdu, offset);
        SendControlFrame(J1939Pgn.TpDt, dt, destinationAddress: J1939TpFrames.GlobalDestinationAddress,
            session, onConfirmed: () => OnBamDtConfirmed(key, sn));
    }

    private void OnBamDtConfirmed(TxSessionKey key, byte confirmedSn)
    {
        if (!_txSessions.TryGetValue(key, out var session) || session.IsCm) return;
        if (session.NextSn != confirmedSn) return; // stale confirmation

        // Compute the next SN as an int first: for a maximum-length PDU (TotalPackets=255) the
        // last SN is 255 and incrementing a byte past 255 wraps to 0, which would fool the
        // "sn > TotalPackets" completion check below into looping forever.
        int nextSn = confirmedSn + 1;
        if (nextSn > session.TotalPackets)
        {
            // BAM has no ack -- complete once every DT has been transmitted.
            _txSessions.Remove(key);
            session.Tcs.TrySetResult(null);
            return;
        }

        session.NextSn = (byte)nextSn;
        // Th hold-off between two consecutive BAM DTs (J1939-21 §5.10.3, 50..200 ms).
        _actor.Schedule(_options.Th, () => TrySendNextBamDt(key));
    }

    private void TrySendNextCmDt(TxSessionKey key)
    {
        if (!_txSessions.TryGetValue(key, out var session) || !session.IsCm) return;
        int offset = (session.NextSn - 1) * J1939TpFrames.DtDataBytes;
        byte sn = session.NextSn;
        var dt = J1939TpFrames.BuildDt(sn, session.Pdu, offset);
        SendControlFrame(J1939Pgn.TpDt, dt, destinationAddress: key.DestinationAddress,
            session, onConfirmed: () => OnCmDtConfirmed(key, sn));
    }

    private void OnCmDtConfirmed(TxSessionKey key, byte confirmedSn)
    {
        if (!_txSessions.TryGetValue(key, out var session) || !session.IsCm) return;
        if (session.NextSn != confirmedSn) return; // stale confirmation

        // Same overflow discipline as OnBamDtConfirmed: an int keeps the "we finished" test
        // meaningful even when TotalPackets=255.
        int nextSn = confirmedSn + 1;
        session.NextSn = (byte)nextSn;
        session.BlockRemaining--;
        int sentPackets = confirmedSn;

        if (sentPackets >= session.TotalPackets)
        {
            // Last packet -- wait for EndOfMsgAck (T3).
            session.State = TxStage.WaitEom;
            session.Deadline?.Dispose();
            session.Deadline = _deadlines.Arm(_options.T3, () => OnTxT3Expired(key));
            return;
        }

        if (session.BlockRemaining <= 0)
        {
            // Block done. If the peer already cleared the next block (early CTS race on a fast
            // Virtual bus), apply the stashed CTS immediately; otherwise wait for T2.
            if (session.HasPendingCts)
            {
                session.HasPendingCts = false;
                session.State = TxStage.SendingDt;
                session.NextSn = session.PendingCtsNextSn;
                session.BlockRemaining = session.PendingCtsNumPackets;
                session.Deadline?.Dispose();
                session.Deadline = null;
                TrySendNextCmDt(key);
                return;
            }

            session.State = TxStage.WaitCts;
            session.Deadline?.Dispose();
            session.Deadline = _deadlines.Arm(_options.T2, () => OnTxT2Expired(key));
            return;
        }

        // Send the next DT in the same block; chained via confirmation so DTs cannot race each
        // other in the underlying Task.Run pool (which would otherwise let SN N+1 hit the wire
        // before SN N when the transport is very fast, as happens on virtual/loopback buses).
        TrySendNextCmDt(key);
    }

    private void OnTxT2Expired(TxSessionKey key)
    {
        if (!_txSessions.TryGetValue(key, out var session) || !session.IsCm) return;
        if (session.State != TxStage.WaitCts) return;
        AbortTx(session, J1939TpAbortReason.Timeout,
            "T2 expired waiting for next CTS from peer.");
    }

    private void OnTxT3Expired(TxSessionKey key)
    {
        if (!_txSessions.TryGetValue(key, out var session) || !session.IsCm) return;
        if (session.State != TxStage.WaitCts && session.State != TxStage.WaitEom) return;
        var msg = session.State == TxStage.WaitEom
            ? "T3 expired waiting for EndOfMsgAck from peer."
            : "T3 expired waiting for initial CTS from peer.";
        AbortTx(session, J1939TpAbortReason.Timeout, msg);
    }

    private void OnTxT4Expired(TxSessionKey key)
    {
        if (!_txSessions.TryGetValue(key, out var session) || !session.IsCm) return;
        if (session.State != TxStage.WaitCts) return;
        AbortTx(session, J1939TpAbortReason.Timeout,
            "T4 expired: peer held session open (CTS with numPackets=0) too long.");
    }

    private void AbortTx(TxSession session, J1939TpAbortReason reason, string message)
    {
        _txSessions.Remove(session.Key);
        session.Deadline?.Dispose();
        session.Deadline = null;
        // Notify peer.
        SendTpCm(J1939TpFrames.BuildAbort(reason, session.Key.Pgn),
            destinationAddress: session.Key.DestinationAddress);
        session.Tcs.TrySetException(new J1939TpAbortException(reason, session.Key.Pgn, message));
    }

    // =========================================================================================
    // Wire helpers
    // =========================================================================================
    private void SendTpCm(byte[] payload, byte destinationAddress)
        => SendControlFrame(J1939Pgn.TpCm, payload, destinationAddress, session: null, onConfirmed: null);

    private void SendControlFrame(uint pgn, byte[] payload, byte destinationAddress,
        TxSession? session, Action? onConfirmed)
    {
        uint canId = J1939Id.ComposePgn(_options.Priority, pgn, _sourceAddress, destinationAddress);
        var frame = CanFrame.Classic(unchecked((int)canId), payload, isExtendedFrame: true);

        _ = Task.Run(async () =>
        {
            try
            {
                var confirmation = await _service.SendConfirmed(frame).ConfigureAwait(false);
                if (!confirmation.Confirmed)
                {
                    // TX rejection / timeout at L2 -- surface via the owning session (if any),
                    // otherwise via the background channel. We do NOT retry: J1939-TP has no
                    // retransmission (§5.10.2.4 "no retry").
                    var ex = confirmation.FailureReason switch
                    {
                        TxConfirmFailureReason.Rejected => (Exception)new J1939TpSendRejectedException(
                            "CAN driver rejected a J1939-TP frame."),
                        TxConfirmFailureReason.BusOff => new J1939TpException("CAN bus went BusOff during J1939-TP transmission."),
                        _ => new J1939TpException($"J1939-TP frame TX confirmation failed: {confirmation.FailureReason}."),
                    };
                    _actor.Post(() =>
                    {
                        if (session is not null && _txSessions.TryGetValue(session.Key, out var current) && ReferenceEquals(current, session))
                        {
                            _txSessions.Remove(session.Key);
                            session.Deadline?.Dispose();
                            session.Deadline = null;
                            session.Tcs.TrySetException(ex);
                        }
                        else
                        {
                            RaiseBackgroundException(ex);
                        }
                    });
                    return;
                }

                // Success: hop onto the actor and run the caller's continuation there so the
                // next-DT chain stays single-writer-safe alongside the rest of the session state.
                if (onConfirmed is not null)
                {
                    try { _actor.Post(onConfirmed); }
                    catch (ObjectDisposedException) { /* actor gone; nothing to do */ }
                }
            }
            catch (Exception ex)
            {
                try { _actor.Post(() => RaiseBackgroundException(ex)); }
                catch (ObjectDisposedException) { /* actor gone; nothing to do */ }
            }
        });
    }

    // =========================================================================================
    // Misc
    // =========================================================================================
    private void EmitPdu(J1939TpDatagram datagram)
    {
        try
        {
            DatagramReceived?.Invoke(this, datagram);
        }
        catch (Exception ex)
        {
            RaiseBackgroundException(ex);
        }
        _pduInbox.Writer.TryWrite(datagram);
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
            throw new ObjectDisposedException(nameof(J1939TpChannel));
    }

    // =========================================================================================
    // Nested types
    // =========================================================================================
    private readonly struct TxSessionKey : IEquatable<TxSessionKey>
    {
        public TxSessionKey(byte destinationAddress, uint pgn)
        {
            DestinationAddress = destinationAddress;
            Pgn = pgn;
        }

        public byte DestinationAddress { get; }
        public uint Pgn { get; }

        public bool Equals(TxSessionKey other) => DestinationAddress == other.DestinationAddress && Pgn == other.Pgn;
        public override bool Equals(object? obj) => obj is TxSessionKey k && Equals(k);
        public override int GetHashCode() => unchecked((DestinationAddress * 397) ^ (int)Pgn);
    }

    // (peer SA, kind) uniquely identifies an inbound TP session per J1939-21 §5.10.3: at most
    // one CM connection and one BAM per source. This is the *only* keying TP.DT can honor since
    // DT frames carry no PGN -- see class remarks.
    private readonly struct RxSessionKey : IEquatable<RxSessionKey>
    {
        public RxSessionKey(byte sourceAddress, J1939TpKind kind)
        {
            SourceAddress = sourceAddress;
            Kind = kind;
        }

        public byte SourceAddress { get; }
        public J1939TpKind Kind { get; }

        public bool Equals(RxSessionKey other) => SourceAddress == other.SourceAddress && Kind == other.Kind;
        public override bool Equals(object? obj) => obj is RxSessionKey k && Equals(k);
        public override int GetHashCode() => unchecked((SourceAddress * 397) ^ (int)Kind);
    }

    private enum TxStage : byte { WaitCts, SendingDt, WaitEom }

    private sealed class TxSession
    {
        public TxSession(TxSessionKey key, byte[] pdu, int totalPackets, TaskCompletionSource<object?> tcs, bool isCm)
        {
            Key = key;
            Pdu = pdu;
            TotalPackets = totalPackets;
            Tcs = tcs;
            IsCm = isCm;
            State = isCm ? TxStage.WaitCts : TxStage.SendingDt;
        }

        public TxSessionKey Key { get; }
        public byte[] Pdu { get; }
        public int TotalPackets { get; }
        public TaskCompletionSource<object?> Tcs { get; }
        public bool IsCm { get; }
        public TxStage State { get; set; }
        public byte NextSn { get; set; }
        public int BlockRemaining { get; set; }
        public IDeadline? Deadline { get; set; }

        /// <summary>
        /// Set when a CTS for the next block arrives before the last DT of the current block has
        /// been confirmed (Virtual-loopback race). Applied in <see cref="OnCmDtConfirmed"/>.
        /// </summary>
        public bool HasPendingCts { get; set; }
        public byte PendingCtsNumPackets { get; set; }
        public byte PendingCtsNextSn { get; set; }

        public void Cancel()
        {
            Deadline?.Dispose();
            Deadline = null;
            Tcs.TrySetCanceled();
        }

        public void Fail(Exception ex)
        {
            Deadline?.Dispose();
            Deadline = null;
            Tcs.TrySetException(ex);
        }
    }

    private sealed class RxSession
    {
        private readonly Action<RxSession> _onT1;
        private readonly Action<RxSession>? _onTr;
        private readonly TimeSpan _t1;
        private readonly TimeSpan _tr;
        private readonly DeadlineScheduler _scheduler;
        private bool _awaitingFirstDtOfBlock;

        private RxSession(byte peer, uint pgn, byte[] buffer, int totalPackets, byte maxPacketsPerCts,
            J1939TpKind kind, DeadlineScheduler scheduler, TimeSpan t1, TimeSpan tr,
            Action<RxSession> onT1, Action<RxSession>? onTr, bool armT1Immediately)
        {
            PeerAddress = peer;
            Pgn = pgn;
            Buffer = buffer;
            TotalPackets = totalPackets;
            MaxPacketsPerCts = maxPacketsPerCts;
            Kind = kind;
            _scheduler = scheduler;
            _t1 = t1;
            _tr = tr;
            _onT1 = onT1;
            _onTr = onTr;
            if (armT1Immediately)
                Deadline = scheduler.Arm(t1, () => onT1(this));
        }

        public static RxSession NewBam(byte sa, uint pgn, int totalBytes, int totalPackets,
            DeadlineScheduler scheduler, J1939TpOptions options, Action<RxSession> onT1)
            => new(sa, pgn, new byte[totalBytes], totalPackets, options.MaxPacketsPerCts,
                J1939TpKind.Bam, scheduler, options.T1, options.Tr, onT1, onTr: null,
                armT1Immediately: true)
            { NextExpectedSn = 1 };

        public static RxSession NewCm(byte sa, uint pgn, int totalBytes, int totalPackets, byte maxPerCts,
            DeadlineScheduler scheduler, J1939TpOptions options,
            Action<RxSession> onT1, Action<RxSession> onTr)
            // CM does not arm T1 here: the caller sends CTS then calls ArmTr() so Tr covers the
            // CTS → first-DT gap (options.Tr / FR-TP-032). T1 takes over inside the block.
            => new(sa, pgn, new byte[totalBytes], totalPackets, maxPerCts,
                J1939TpKind.Cm, scheduler, options.T1, options.Tr, onT1, onTr,
                armT1Immediately: false)
            { NextExpectedSn = 1 };

        public byte PeerAddress { get; }
        public uint Pgn { get; }
        public byte[] Buffer { get; }
        public int TotalPackets { get; }
        public byte MaxPacketsPerCts { get; }
        public J1939TpKind Kind { get; }
        public byte NextExpectedSn { get; set; }
        public int BlockRemaining { get; set; }
        public int PacketsReceived { get; set; }
        public IDeadline? Deadline { get; private set; }

        /// <summary>
        /// Arms Tr after emitting a CTS: peer must deliver the first TP.DT of the granted block
        /// before Tr elapses (J1939TpOptions.Tr / §5.10.2.4).
        /// </summary>
        public void ArmTr()
        {
            if (_onTr is null) return;
            Deadline?.Dispose();
            _awaitingFirstDtOfBlock = true;
            Deadline = _scheduler.Arm(_tr, () => _onTr(this));
        }

        /// <summary>
        /// Called on every accepted TP.DT. For CM, the first DT of a CTS block clears Tr and
        /// switches to T1; subsequent DTs rearm T1. BAM always rearms T1.
        /// </summary>
        public void OnDtReceived()
        {
            if (_awaitingFirstDtOfBlock)
            {
                _awaitingFirstDtOfBlock = false;
                Deadline?.Dispose();
                Deadline = _scheduler.Arm(_t1, () => _onT1(this));
                return;
            }
            RearmT1();
        }

        public void RearmT1()
        {
            var existing = Deadline;
            if (existing is not null && !existing.IsExpired && !existing.IsCancelled)
            {
                if (existing.Rearm(_t1)) return;
            }
            existing?.Dispose();
            Deadline = _scheduler.Arm(_t1, () => _onT1(this));
        }

        public void Cancel()
        {
            Deadline?.Dispose();
            Deadline = null;
            _awaitingFirstDtOfBlock = false;
        }
    }
}
