using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CanKit.Pro.CANopen.Sdo;
using CanKit.Pro.Reliability;

namespace CanKit.Pro.CANopen;

/// <summary>
/// SDO block-transfer (CiA 301 §7.2.4.3.15, FR-CO-004) partial of <see cref="CanOpenNode"/>.
/// Implements both client and server sides for block download and block upload, using the
/// same actor loop / deadline scheduler / <see cref="CanKit.Pro.RawCan.ICanBusService"/> as
/// the rest of the node.
/// </summary>
/// <remarks>
/// <para>Threading: every state read/write in this file happens on the actor loop. Public entry
/// points (<c>BeginSdoBlock*</c>) are only ever invoked from posted actor actions, and
/// incoming-frame handlers are called from <see cref="CanOpenNode.HandleIncoming"/> which is
/// itself a posted actor action.</para>
/// <para>Session lifecycle mirrors the classical SDO client / server: at most one client-side
/// transfer per remote server (keyed by node-id), and at most one server-side transfer for
/// our own OD. A stale server-side transfer is superseded on any fresh initiate (CiA 301
/// §7.2.4.3.4) — matching what the plain SDO server already does.</para>
/// </remarks>
internal sealed partial class CanOpenNode
{
    // =========================================================================================
    // Client — block download (we send our payload byte-stream to the peer's OD).
    // =========================================================================================
    private void BeginSdoBlockDownload(byte serverNodeId, ushort index, byte subindex,
        byte[] payload, TaskCompletionSource<byte[]> tcs)
    {
        if (_disposed != 0)
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(CanOpenNode)));
            return;
        }
        if (tcs.Task.IsCompleted) return;
        if (_sdoClients.ContainsKey(serverNodeId) || _sdoBlockClients.ContainsKey(serverNodeId))
        {
            tcs.TrySetException(new InvalidOperationException(
                $"An SDO transfer with server 0x{serverNodeId:X2} is already in flight."));
            return;
        }
        if (payload.Length > _options.MaxSdoTransferBytes)
        {
            tcs.TrySetException(new SdoAbortException(index, subindex, SdoAbortCode.OutOfMemory));
            return;
        }

        var session = new SdoBlockClientSession(serverNodeId, index, subindex,
            isDownload: true, payload, tcs, _options.SdoBlockCrcSupported);
        _sdoBlockClients[serverNodeId] = session;
        session.Deadline = _deadlines.Arm(_options.SdoTimeout,
            () => OnSdoBlockClientTimeout(serverNodeId));

        var init = SdoBlockFrames.BuildBlockDownloadInit(index, subindex,
            clientCrcSupported: session.LocalCrcSupported,
            sizeIndicated: true,
            totalSize: (uint)payload.Length);
        _ = SendControlFrame(CanOpenCobId.SdoRx(serverNodeId), init);
    }

    // =========================================================================================
    // Client — block upload (we ask the peer to stream its OD value back to us).
    // =========================================================================================
    private void BeginSdoBlockUpload(byte serverNodeId, ushort index, byte subindex,
        TaskCompletionSource<byte[]> tcs)
    {
        if (_disposed != 0)
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(CanOpenNode)));
            return;
        }
        if (tcs.Task.IsCompleted) return;
        if (_sdoClients.ContainsKey(serverNodeId) || _sdoBlockClients.ContainsKey(serverNodeId))
        {
            tcs.TrySetException(new InvalidOperationException(
                $"An SDO transfer with server 0x{serverNodeId:X2} is already in flight."));
            return;
        }

        var session = new SdoBlockClientSession(serverNodeId, index, subindex,
            isDownload: false, payload: null, tcs, _options.SdoBlockCrcSupported);
        session.LocalBlockSize = _options.SdoBlockSize;
        _sdoBlockClients[serverNodeId] = session;
        session.Deadline = _deadlines.Arm(_options.SdoTimeout,
            () => OnSdoBlockClientTimeout(serverNodeId));

        // pst=0 disables the CiA 301 fallback to segmented transfer. We do not currently
        // implement the fallback and setting pst=0 keeps peers from ever forcing us into it.
        var init = SdoBlockFrames.BuildBlockUploadInit(index, subindex,
            clientCrcSupported: session.LocalCrcSupported,
            blockSize: session.LocalBlockSize,
            pst: 0);
        _ = SendControlFrame(CanOpenCobId.SdoRx(serverNodeId), init);
    }

    private void OnSdoBlockClientTimeout(byte serverNodeId)
    {
        if (!_sdoBlockClients.TryGetValue(serverNodeId, out var session)) return;
        _sdoBlockClients.Remove(serverNodeId);
        session.Deadline?.Dispose();
        _ = SendControlFrame(CanOpenCobId.SdoRx(serverNodeId),
            SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)SdoAbortCode.SdoProtocolTimedOut));
        session.Tcs.TrySetException(new SdoAbortException(session.Index, session.Subindex,
            SdoAbortCode.SdoProtocolTimedOut));
    }

    private void RearmBlockClient(SdoBlockClientSession session, byte serverNodeId)
    {
        var deadline = session.Deadline;
        if (deadline is null || deadline.IsExpired || deadline.IsCancelled || !deadline.Rearm(_options.SdoTimeout))
        {
            deadline?.Dispose();
            session.Deadline = _deadlines.Arm(_options.SdoTimeout, () => OnSdoBlockClientTimeout(serverNodeId));
        }
    }

    private void AbortBlockClient(SdoBlockClientSession session, SdoAbortCode code)
    {
        _sdoBlockClients.Remove(session.ServerNodeId);
        session.Deadline?.Dispose();
        _ = SendControlFrame(CanOpenCobId.SdoRx(session.ServerNodeId),
            SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)code));
        session.Tcs.TrySetException(new SdoAbortException(session.Index, session.Subindex, code));
    }

    /// <summary>
    /// Handles a frame arriving on <c>0x580 + serverNodeId</c> when a block-transfer client
    /// session against that server is open. Returns true if this handler consumed the frame,
    /// false if the caller should fall back to the classical <c>HandleSdoClientResponse</c>.
    /// </summary>
    private bool HandleSdoClientResponseBlock(byte serverNodeId, byte[] data)
    {
        if (!_sdoBlockClients.TryGetValue(serverNodeId, out var session)) return false;
        if (data.Length == 0) return true; // consume — nothing to parse

        // Pad short DLC frames back to 8 bytes for parsing, matching the classic SDO client
        // path's behaviour with DLC-stripping ECUs.
        if (data.Length < 8)
        {
            var padded = new byte[8];
            Buffer.BlockCopy(data, 0, padded, 0, data.Length);
            data = padded;
        }

        byte cs = data[0];

        // Explicit abort from the peer always wins, regardless of phase.
        if (cs == SdoFrames.CsAbort)
        {
            var (idx, sub) = SdoFrames.ReadIndex(data);
            uint code = SdoFrames.ReadAbortCode(data);
            if (idx != session.Index || sub != session.Subindex) return true;
            _sdoBlockClients.Remove(serverNodeId);
            session.Deadline?.Dispose();
            session.Tcs.TrySetException(new SdoAbortException(idx, sub, code,
                $"Peer server 0x{serverNodeId:X2} aborted SDO block transfer 0x{idx:X4}:{sub:X2} with code 0x{code:X8}."));
            return true;
        }

        RearmBlockClient(session, serverNodeId);

        if (session.IsDownload)
        {
            return HandleBlockDownloadClientFrame(session, data, cs);
        }
        return HandleBlockUploadClientFrame(session, data, cs);
    }

    private bool HandleBlockDownloadClientFrame(SdoBlockClientSession session, byte[] data, byte cs)
    {
        switch (session.Phase)
        {
            case SdoBlockClientPhase.AwaitInitResponse:
                // Expect 0xA0 | (sc<<2). Reject anything else.
                if ((cs & 0xE3) != ScsInitResponseMaskDownload)
                {
                    AbortBlockClient(session, SdoAbortCode.CommandSpecifierInvalid);
                    return true;
                }
                {
                    byte serverBlkSize = data[4];
                    if (serverBlkSize is < 1 or > 127)
                    {
                        AbortBlockClient(session, SdoAbortCode.InvalidBlockSize);
                        return true;
                    }
                    // CRC is exchanged only when both endpoints advertised support (CiA 301).
                    session.CrcActive = session.LocalCrcSupported && SdoBlockFrames.ReadCrcSupportedBit(cs);
                    session.NegotiatedBlockSize = serverBlkSize;
                    session.Phase = SdoBlockClientPhase.SendingSegments;
                    SendNextBlockDownloadSubBlock(session);
                }
                return true;

            case SdoBlockClientPhase.AwaitSubBlockAck:
                if (cs != ScsBlockDownloadSubBlockAck)
                {
                    AbortBlockClient(session, SdoAbortCode.CommandSpecifierInvalid);
                    return true;
                }
                {
                    var (ackseq, nextBlkSize) = SdoBlockFrames.ReadSubBlockAck(data);
                    // ackseq is the number of segments the server accepted from the last
                    // sub-block; anything less than SegmentsInFlight means we need to rewind.
                    // In MVP: reject partial acks (ackseq != SegmentsInFlight) with an abort
                    // rather than implement full retransmission. On our loopback bus this
                    // path is exercised only for the "server picks smaller blksize" test,
                    // where ackseq == SegmentsInFlight is guaranteed.
                    if (ackseq != session.SegmentsInFlight)
                    {
                        AbortBlockClient(session, SdoAbortCode.General);
                        return true;
                    }
                    if (nextBlkSize is < 1 or > 127)
                    {
                        AbortBlockClient(session, SdoAbortCode.InvalidBlockSize);
                        return true;
                    }
                    session.NegotiatedBlockSize = nextBlkSize;
                    if (session.Offset >= session.Payload!.Length)
                    {
                        SendBlockDownloadEnd(session);
                    }
                    else
                    {
                        session.Phase = SdoBlockClientPhase.SendingSegments;
                        SendNextBlockDownloadSubBlock(session);
                    }
                }
                return true;

            case SdoBlockClientPhase.AwaitEndResponse:
                if (cs != ScsBlockDownloadEndResponse)
                {
                    AbortBlockClient(session, SdoAbortCode.CommandSpecifierInvalid);
                    return true;
                }
                _sdoBlockClients.Remove(session.ServerNodeId);
                session.Deadline?.Dispose();
                session.Tcs.TrySetResult(Array.Empty<byte>());
                return true;

            default:
                AbortBlockClient(session, SdoAbortCode.CommandSpecifierInvalid);
                return true;
        }
    }

    private bool HandleBlockUploadClientFrame(SdoBlockClientSession session, byte[] data, byte cs)
    {
        switch (session.Phase)
        {
            case SdoBlockClientPhase.AwaitInitResponse:
                // Expect scs=6 (0xC0 base) with optional sc / s bits.
                if ((cs & 0xE1) != ScsInitResponseMaskUpload)
                {
                    AbortBlockClient(session, SdoAbortCode.CommandSpecifierInvalid);
                    return true;
                }
                {
                    uint declared = SdoBlockFrames.ReadUploadTotalSize(data);
                    if (declared > (uint)_options.MaxSdoTransferBytes)
                    {
                        AbortBlockClient(session, SdoAbortCode.OutOfMemory);
                        return true;
                    }
                    session.CrcActive = session.LocalCrcSupported && SdoBlockFrames.ReadCrcSupportedBit(cs);
                    session.DeclaredTotalSize = declared;
                    session.Payload = declared > 0 ? new byte[declared] : Array.Empty<byte>();
                    session.Offset = 0;
                    session.NextExpectedSeq = 1;
                    session.SegmentsInFlight = 0;
                    session.Phase = SdoBlockClientPhase.ReceivingSegments;

                    // Tell the server to begin streaming segments.
                    _ = SendControlFrame(CanOpenCobId.SdoRx(session.ServerNodeId),
                        SdoBlockFrames.BuildEndResponse(SdoBlockFrames.CcsBlockUploadStart));
                }
                return true;

            case SdoBlockClientPhase.ReceivingSegments:
                // A segment frame (byte 0 = (c<<7)|seq). CiA 301 lets the "end of block"
                // frame come only *after* we have ACKed the last sub-block, so during
                // ReceivingSegments any incoming frame is a segment. The dispatcher in
                // HandleIncoming already prevented block-server segment values from being
                // decoded as regular SDO responses.
                return HandleBlockUploadSegment(session, data, cs);

            case SdoBlockClientPhase.AwaitEnd:
                // Expect 0xC1 | (n<<2) with CRC in bytes 1..2.
                if ((cs & 0xE3) != ScsBlockUploadEndMask)
                {
                    AbortBlockClient(session, SdoAbortCode.CommandSpecifierInvalid);
                    return true;
                }
                {
                    byte n = SdoBlockFrames.ReadEndUnusedBytes(cs);
                    ushort peerCrc = SdoBlockFrames.ReadEndCrc(data);
                    // The last stashed segment held 7 bytes of raw data; drop n of them.
                    if (n > 0)
                    {
                        if (session.Offset < n)
                        {
                            AbortBlockClient(session, SdoAbortCode.General);
                            return true;
                        }
                        session.Offset -= n;
                    }

                    if (session.DeclaredTotalSize > 0 && session.Offset != session.DeclaredTotalSize)
                    {
                        AbortBlockClient(session,
                            session.Offset > session.DeclaredTotalSize
                                ? SdoAbortCode.LengthTooHigh
                                : SdoAbortCode.LengthTooLow);
                        return true;
                    }

                    var final = new byte[session.Offset];
                    Buffer.BlockCopy(session.Payload!, 0, final, 0, session.Offset);

                    if (session.CrcActive)
                    {
                        var localCrc = SdoBlockFrames.ComputeCrc16Xmodem(final);
                        if (localCrc != peerCrc)
                        {
                            AbortBlockClient(session, SdoAbortCode.CrcError);
                            return true;
                        }
                    }

                    // Acknowledge end and complete the transfer.
                    _ = SendControlFrame(CanOpenCobId.SdoRx(session.ServerNodeId),
                        SdoBlockFrames.BuildEndResponse(SdoBlockFrames.CcsBlockUploadEndResponse));
                    _sdoBlockClients.Remove(session.ServerNodeId);
                    session.Deadline?.Dispose();
                    session.Tcs.TrySetResult(final);
                }
                return true;

            default:
                AbortBlockClient(session, SdoAbortCode.CommandSpecifierInvalid);
                return true;
        }
    }

    private bool HandleBlockUploadSegment(SdoBlockClientSession session, byte[] data, byte cs)
    {
        byte seq = (byte)(cs & 0x7F);
        bool last = (cs & 0x80) != 0;

        if (seq != session.NextExpectedSeq)
        {
            // Out-of-order segment: NACK by sending an ack for the last good seq and
            // requesting the same blksize again. The peer restarts at ackseq + 1 within
            // the same sub-block (CiA 301), so keep NextExpectedSeq / SegmentsInFlight —
            // resetting them to 1/0 would reject the retransmission stream.
            var ack = SdoBlockFrames.BuildSubBlockAck(SdoBlockFrames.CcsBlockUploadSubBlockAck,
                lastAckedSeq: (byte)(session.NextExpectedSeq - 1),
                nextBlockSize: session.LocalBlockSize);
            _ = SendControlFrame(CanOpenCobId.SdoRx(session.ServerNodeId), ack);
            return true;
        }

        int room = _options.MaxSdoTransferBytes - session.Offset;
        if (room < 7)
        {
            AbortBlockClient(session, SdoAbortCode.OutOfMemory);
            return true;
        }
        if (session.Payload!.Length - session.Offset < 7)
        {
            // Grow when the declared size was 0 (unbounded) or when the payload was under-declared.
            int needed = session.Offset + 7;
            if (needed > _options.MaxSdoTransferBytes)
            {
                AbortBlockClient(session, SdoAbortCode.OutOfMemory);
                return true;
            }
            var grown = new byte[needed];
            Buffer.BlockCopy(session.Payload, 0, grown, 0, session.Payload.Length);
            session.Payload = grown;
        }
        // Copy the full 7 data bytes; unused bytes in the *last* segment are trimmed off later
        // using the "n" field from the end-of-block frame.
        Buffer.BlockCopy(data, 1, session.Payload, session.Offset, 7);
        session.Offset += 7;
        session.NextExpectedSeq = (byte)(seq + 1);
        session.SegmentsInFlight++;

        if (last || session.SegmentsInFlight >= session.LocalBlockSize)
        {
            // Send sub-block ACK (last successfully received seqno, next blksize we want).
            var ack = SdoBlockFrames.BuildSubBlockAck(SdoBlockFrames.CcsBlockUploadSubBlockAck,
                lastAckedSeq: seq,
                nextBlockSize: session.LocalBlockSize);
            _ = SendControlFrame(CanOpenCobId.SdoRx(session.ServerNodeId), ack);
            session.NextExpectedSeq = 1;
            session.SegmentsInFlight = 0;
            if (last)
            {
                session.Phase = SdoBlockClientPhase.AwaitEnd;
            }
        }
        return true;
    }

    private void SendNextBlockDownloadSubBlock(SdoBlockClientSession session)
    {
        // Prepare all segments for this sub-block up front so they can be sent as a single
        // ordered Task chain (via SendOrderedControlFrames). We can NOT dispatch each
        // segment through the per-frame SendControlFrame helper because that Task.Runs
        // each send, and 127 concurrent Task.Runs would arrive at the peer in unspecified
        // order — the block-transfer receiver enforces monotonically increasing seqnos, so
        // any reordering aborts the whole transfer with SdoAbortCode.General.
        int segments = 0;
        var frames = new List<(uint CobId, byte[] Payload)>(session.NegotiatedBlockSize);
        uint cobId = CanOpenCobId.SdoRx(session.ServerNodeId);
        while (segments < session.NegotiatedBlockSize)
        {
            int remaining = session.Payload!.Length - session.Offset;
            if (remaining <= 0) break;

            int chunk = Math.Min(7, remaining);
            var segData = new byte[chunk];
            Buffer.BlockCopy(session.Payload, session.Offset, segData, 0, chunk);
            session.Offset += chunk;
            segments++;

            bool isLastOverall = session.Offset >= session.Payload.Length;
            byte seqno = (byte)segments;
            var frame = SdoBlockFrames.BuildSegment(seqno, isLastSegment: isLastOverall, segData);
            frames.Add((cobId, frame));

            if (isLastOverall)
            {
                session.LastSegmentUnusedBytes = (byte)(7 - chunk);
                break;
            }
        }
        session.SegmentsInFlight = (byte)segments;
        session.Phase = SdoBlockClientPhase.AwaitSubBlockAck;
        _ = SendOrderedControlFrames(frames.ToArray());
    }

    private void SendBlockDownloadEnd(SdoBlockClientSession session)
    {
        ushort crc = 0;
        if (session.CrcActive)
        {
            var full = new byte[session.Payload!.Length];
            Buffer.BlockCopy(session.Payload, 0, full, 0, full.Length);
            crc = SdoBlockFrames.ComputeCrc16Xmodem(full);
        }
        var end = SdoBlockFrames.BuildEnd(SdoBlockFrames.CcsBlockDownloadEndBase,
            session.LastSegmentUnusedBytes, crc);
        session.Phase = SdoBlockClientPhase.AwaitEndResponse;
        _ = SendControlFrame(CanOpenCobId.SdoRx(session.ServerNodeId), end);
    }

    // =========================================================================================
    // Server — block download (peer sends a payload to our OD).
    // =========================================================================================
    /// <summary>
    /// Called from <c>HandleIncoming</c> for a frame on our SDO Rx COB-ID *before* the classic
    /// <c>HandleSdoServerRequest</c>. Returns true if this handler consumed the frame — that
    /// is, either it was a block-transfer initiate for us, or a follow-up frame for an
    /// already-open block-server session.
    /// </summary>
    private bool HandleSdoServerRequestBlock(byte[] data)
    {
        // CiA 301: SDO (including block transfer) is not available in Stopped / Initializing.
        // Mirror HandleSdoServerRequest so block initiates and in-flight segment handling are
        // dropped the same way as classic SDO.
        if (_state is NmtState.Stopped or NmtState.Initializing)
            return false;

        if (data.Length == 0) return false;

        // Pad short DLC frames back to 8 bytes for parsing.
        if (data.Length < 8)
        {
            var padded = new byte[8];
            Buffer.BlockCopy(data, 0, padded, 0, data.Length);
            data = padded;
        }
        byte cs = data[0];

        // An abort during any active block-server phase tears the block session down and lets
        // the caller (nothing more to do at the classical layer since the peer is aborting our
        // OWN transfer). Handled here explicitly because in ReceivingSegments the segment
        // stream and cs=0x80 (abort) are otherwise indistinguishable without the "seq=0 is
        // invalid" hint.
        if (_sdoBlockServer is not null && cs == SdoFrames.CsAbort)
        {
            var stale = _sdoBlockServer;
            _sdoBlockServer = null;
            stale.Deadline?.Dispose();
            return true;
        }

        // A block-server session in a segment-receiving phase intercepts every frame on our
        // SDO Rx (until the peer emits the "end of block" frame). This is what "phase-scoped"
        // dispatch means: byte 0 = (c<<7)|seq overlaps command specifiers, so we cannot look
        // at cs to decide "is this a segment".
        if (_sdoBlockServer is { Phase: SdoBlockServerPhase.ReceivingSegments } rx)
        {
            HandleBlockDownloadServerSegment(rx, data, cs);
            return true;
        }
        if (_sdoBlockServer is { Phase: SdoBlockServerPhase.AwaitEnd } awaitingEnd
            && (cs & 0xE3) == CcsBlockDownloadEndMask)
        {
            HandleBlockDownloadServerEnd(awaitingEnd, data, cs);
            return true;
        }

        // NMT / block-init: check for a block-transfer initiate (0xC0/0xC4/0xC6 for download,
        // 0xA0/0xA4 for upload). Abort (0x80) is left to the classical server; the block
        // server section only intercepts its own control frames.
        // Block download initiate: ccs=6, cs=0. bits 5..7 = 110, bit 0 = 0.
        if ((cs & 0xE1) == CcsBlockDownloadInitMask)
        {
            HandleBlockDownloadServerInit(data, cs);
            return true;
        }
        // Block upload initiate: ccs=5, cs=0. bits 5..7 = 101, bits 0..1 = 00.
        if ((cs & 0xE3) == CcsBlockUploadInitMask)
        {
            HandleBlockUploadServerInit(data, cs);
            return true;
        }
        // Block upload start (0xA3), sub-block ack (0xA2), end response (0xA1).
        if (cs == SdoBlockFrames.CcsBlockUploadStart && _sdoBlockServer is { InDownload: false } up)
        {
            HandleBlockUploadServerStart(up);
            return true;
        }
        if (cs == SdoBlockFrames.CcsBlockUploadSubBlockAck
            && _sdoBlockServer is { InDownload: false, Phase: SdoBlockServerPhase.AwaitSubBlockAck } upAck)
        {
            HandleBlockUploadServerSubBlockAck(upAck, data);
            return true;
        }
        if (cs == SdoBlockFrames.CcsBlockUploadEndResponse
            && _sdoBlockServer is { InDownload: false, Phase: SdoBlockServerPhase.AwaitEndResponse } upEndResp)
        {
            _sdoBlockServer = null;
            upEndResp.Deadline?.Dispose();
            return true;
        }
        return false;
    }

    private void HandleBlockDownloadServerInit(byte[] data, byte cs)
    {
        AbortSupersededBlockServerSession();
        AbortSupersededServerSession();

        var (index, subindex) = SdoFrames.ReadIndex(data);
        _od.TryGet(index, subindex, out var entry);
        if (entry is null)
        {
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                SdoFrames.BuildAbort(index, subindex, (uint)SdoAbortCode.ObjectDoesNotExist));
            return;
        }
        if ((entry.Access & OdAccess.WriteOnly) == 0)
        {
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                SdoFrames.BuildAbort(index, subindex, (uint)SdoAbortCode.AttemptWriteReadOnly));
            return;
        }

        bool sizeIndicated = (cs & 0x02) != 0;
        uint declaredLen = 0;
        if (sizeIndicated)
        {
            declaredLen = (uint)(data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24));
            if (declaredLen > (uint)_options.MaxSdoTransferBytes)
            {
                _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                    SdoFrames.BuildAbort(index, subindex, (uint)SdoAbortCode.OutOfMemory));
                return;
            }
        }

        bool crcActive = _options.SdoBlockCrcSupported && SdoBlockFrames.ReadCrcSupportedBit(cs);
        byte blkSize = _options.SdoBlockSize;

        int bufSize = sizeIndicated ? (int)declaredLen : 128;
        var session = new SdoBlockServerSession(
            inDownload: true,
            index, subindex,
            buffer: new byte[bufSize],
            offset: 0)
        {
            CrcActive = crcActive,
            NegotiatedBlockSize = blkSize,
            NextExpectedSeq = 1,
            SegmentsInSubBlock = 0,
            DeclaredTotalSize = declaredLen,
            SizeIndicated = sizeIndicated,
            Phase = SdoBlockServerPhase.ReceivingSegments,
        };
        _sdoBlockServer = session;
        session.Deadline = _deadlines.Arm(_options.SdoTimeout, OnSdoBlockServerTimeout);

        // Advertise local CRC capability (CiA 301 "sc" bit); CRC stays inactive for this
        // transfer unless both endpoints set their bits (captured in crcActive above).
        var resp = SdoBlockFrames.BuildBlockDownloadInitResponse(index, subindex,
            serverCrcSupported: _options.SdoBlockCrcSupported, blockSize: blkSize);
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), resp);
    }

    private void HandleBlockDownloadServerSegment(SdoBlockServerSession session, byte[] data, byte cs)
    {
        byte seq = (byte)(cs & 0x7F);
        bool last = (cs & 0x80) != 0;

        if (seq != session.NextExpectedSeq)
        {
            // NACK by sub-block ACK with lastAckedSeq = NextExpectedSeq - 1, prompting
            // retransmission from ackseq + 1 within the current sub-block (CiA 301). Keep
            // NextExpectedSeq / SegmentsInSubBlock so a compliant retransmission is accepted.
            var nack = SdoBlockFrames.BuildSubBlockAck(SdoBlockFrames.ScsBlockDownloadSubBlockAck,
                lastAckedSeq: (byte)(session.NextExpectedSeq - 1),
                nextBlockSize: session.NegotiatedBlockSize);
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), nack);
            return;
        }

        // Ensure room for 7 bytes; grow if declared size was under-specified or unbounded.
        if (session.Offset + 7 > session.Buffer.Length)
        {
            int newLen = session.Offset + 7;
            if (newLen > _options.MaxSdoTransferBytes)
            {
                _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                    SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)SdoAbortCode.OutOfMemory));
                _sdoBlockServer = null;
                session.Deadline?.Dispose();
                return;
            }
            var grown = new byte[newLen];
            Buffer.BlockCopy(session.Buffer, 0, grown, 0, session.Offset);
            session.Buffer = grown;
        }
        Buffer.BlockCopy(data, 1, session.Buffer, session.Offset, 7);
        session.Offset += 7;
        session.NextExpectedSeq = (byte)(seq + 1);
        session.SegmentsInSubBlock++;

        RearmBlockServer(session);

        if (last || session.SegmentsInSubBlock >= session.NegotiatedBlockSize)
        {
            var ack = SdoBlockFrames.BuildSubBlockAck(SdoBlockFrames.ScsBlockDownloadSubBlockAck,
                lastAckedSeq: seq,
                nextBlockSize: session.NegotiatedBlockSize);
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), ack);
            session.SegmentsInSubBlock = 0;
            session.NextExpectedSeq = 1;
            if (last) session.Phase = SdoBlockServerPhase.AwaitEnd;
        }
    }

    private void HandleBlockDownloadServerEnd(SdoBlockServerSession session, byte[] data, byte cs)
    {
        byte n = SdoBlockFrames.ReadEndUnusedBytes(cs);
        ushort peerCrc = SdoBlockFrames.ReadEndCrc(data);
        if (n > 0)
        {
            if (session.Offset < n)
            {
                _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                    SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)SdoAbortCode.General));
                _sdoBlockServer = null;
                session.Deadline?.Dispose();
                return;
            }
            session.Offset -= n;
        }
        if (session.SizeIndicated && session.Offset != session.DeclaredTotalSize)
        {
            var reason = session.Offset > session.DeclaredTotalSize
                ? SdoAbortCode.LengthTooHigh : SdoAbortCode.LengthTooLow;
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)reason));
            _sdoBlockServer = null;
            session.Deadline?.Dispose();
            return;
        }

        var final = new byte[session.Offset];
        Buffer.BlockCopy(session.Buffer, 0, final, 0, session.Offset);

        if (session.CrcActive)
        {
            var localCrc = SdoBlockFrames.ComputeCrc16Xmodem(final);
            if (localCrc != peerCrc)
            {
                _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                    SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)SdoAbortCode.CrcError));
                _sdoBlockServer = null;
                session.Deadline?.Dispose();
                return;
            }
        }

        try { _od.WriteRaw(session.Index, session.Subindex, final); }
        catch
        {
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)SdoAbortCode.General));
            _sdoBlockServer = null;
            session.Deadline?.Dispose();
            return;
        }

        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
            SdoBlockFrames.BuildEndResponse(SdoBlockFrames.ScsBlockDownloadEndResponse));
        _sdoBlockServer = null;
        session.Deadline?.Dispose();
    }

    // =========================================================================================
    // Server — block upload (peer requests our OD value, we stream it out).
    // =========================================================================================
    private void HandleBlockUploadServerInit(byte[] data, byte cs)
    {
        AbortSupersededBlockServerSession();
        AbortSupersededServerSession();

        var (index, subindex) = SdoFrames.ReadIndex(data);
        _od.TryGet(index, subindex, out var entry);
        if (entry is null)
        {
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                SdoFrames.BuildAbort(index, subindex, (uint)SdoAbortCode.ObjectDoesNotExist));
            return;
        }
        if ((entry.Access & OdAccess.ReadOnly) == 0)
        {
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                SdoFrames.BuildAbort(index, subindex, (uint)SdoAbortCode.AttemptReadWriteOnly));
            return;
        }

        if (!_od.TryReadRaw(index, subindex, out var value))
        {
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                SdoFrames.BuildAbort(index, subindex, (uint)SdoAbortCode.ObjectDoesNotExist));
            return;
        }

        // Peer's requested blksize is byte 4; clamp to [1,127].
        byte peerBlkSize = data[4];
        if (peerBlkSize < 1) peerBlkSize = _options.SdoBlockSize;
        if (peerBlkSize > 127) peerBlkSize = 127;

        bool crcActive = _options.SdoBlockCrcSupported && SdoBlockFrames.ReadCrcSupportedBit(cs);

        var session = new SdoBlockServerSession(
            inDownload: false,
            index, subindex,
            buffer: value,
            offset: 0)
        {
            CrcActive = crcActive,
            NegotiatedBlockSize = peerBlkSize,
            DeclaredTotalSize = (uint)value.Length,
            SizeIndicated = true,
            Phase = SdoBlockServerPhase.AwaitStart,
        };
        _sdoBlockServer = session;
        session.Deadline = _deadlines.Arm(_options.SdoTimeout, OnSdoBlockServerTimeout);

        // Advertise local CRC capability (CiA 301 "sc" bit); CRC stays inactive for this
        // transfer unless both endpoints set their bits (captured in crcActive above).
        var resp = SdoBlockFrames.BuildBlockUploadInitResponse(index, subindex,
            serverCrcSupported: _options.SdoBlockCrcSupported, sizeIndicated: true, totalSize: (uint)value.Length);
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), resp);
    }

    private void HandleBlockUploadServerStart(SdoBlockServerSession session)
    {
        session.Phase = SdoBlockServerPhase.SendingSegments;
        SendNextBlockUploadSubBlock(session);
    }

    private void HandleBlockUploadServerSubBlockAck(SdoBlockServerSession session, byte[] data)
    {
        var (ackseq, nextBlkSize) = SdoBlockFrames.ReadSubBlockAck(data);
        if (ackseq != session.SegmentsInSubBlock)
        {
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)SdoAbortCode.General));
            _sdoBlockServer = null;
            session.Deadline?.Dispose();
            return;
        }
        if (nextBlkSize is < 1 or > 127)
        {
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
                SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)SdoAbortCode.InvalidBlockSize));
            _sdoBlockServer = null;
            session.Deadline?.Dispose();
            return;
        }
        session.NegotiatedBlockSize = nextBlkSize;

        if (session.Offset >= session.Buffer.Length)
        {
            SendBlockUploadEnd(session);
            return;
        }
        session.Phase = SdoBlockServerPhase.SendingSegments;
        SendNextBlockUploadSubBlock(session);
    }

    private void SendNextBlockUploadSubBlock(SdoBlockServerSession session)
    {
        // Same ordering caveat as SendNextBlockDownloadSubBlock: send the whole sub-block
        // through SendOrderedControlFrames so seqnos arrive at the peer in order.
        int segments = 0;
        var frames = new List<(uint CobId, byte[] Payload)>(session.NegotiatedBlockSize);
        uint cobId = CanOpenCobId.SdoTx(_nodeId);
        while (segments < session.NegotiatedBlockSize)
        {
            int remaining = session.Buffer.Length - session.Offset;
            if (remaining <= 0) break;
            int chunk = Math.Min(7, remaining);
            var segData = new byte[chunk];
            Buffer.BlockCopy(session.Buffer, session.Offset, segData, 0, chunk);
            session.Offset += chunk;
            segments++;
            bool isLastOverall = session.Offset >= session.Buffer.Length;
            byte seqno = (byte)segments;
            var frame = SdoBlockFrames.BuildSegment(seqno, isLastSegment: isLastOverall, segData);
            frames.Add((cobId, frame));
            if (isLastOverall)
            {
                session.LastSegmentUnusedBytes = (byte)(7 - chunk);
                break;
            }
        }
        session.SegmentsInSubBlock = (byte)segments;
        session.Phase = SdoBlockServerPhase.AwaitSubBlockAck;
        _ = SendOrderedControlFrames(frames.ToArray());
    }

    private void SendBlockUploadEnd(SdoBlockServerSession session)
    {
        ushort crc = 0;
        if (session.CrcActive)
        {
            var full = new byte[session.Buffer.Length];
            Buffer.BlockCopy(session.Buffer, 0, full, 0, full.Length);
            crc = SdoBlockFrames.ComputeCrc16Xmodem(full);
        }
        var end = SdoBlockFrames.BuildEnd(SdoBlockFrames.ScsBlockUploadEndBase,
            session.LastSegmentUnusedBytes, crc);
        session.Phase = SdoBlockServerPhase.AwaitEndResponse;
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), end);
    }

    private void OnSdoBlockServerTimeout()
    {
        var s = _sdoBlockServer;
        if (s is null) return;
        _sdoBlockServer = null;
        s.Deadline?.Dispose();
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
            SdoFrames.BuildAbort(s.Index, s.Subindex, (uint)SdoAbortCode.SdoProtocolTimedOut));
    }

    private void RearmBlockServer(SdoBlockServerSession session)
    {
        var deadline = session.Deadline;
        if (deadline is null || deadline.IsExpired || deadline.IsCancelled || !deadline.Rearm(_options.SdoTimeout))
        {
            deadline?.Dispose();
            session.Deadline = _deadlines.Arm(_options.SdoTimeout, OnSdoBlockServerTimeout);
        }
    }

    private void AbortSupersededBlockServerSession()
    {
        var stale = _sdoBlockServer;
        if (stale is null) return;
        stale.Deadline?.Dispose();
        _sdoBlockServer = null;
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
            SdoFrames.BuildAbort(stale.Index, stale.Subindex, (uint)SdoAbortCode.General));
    }

    // =========================================================================================
    // Command-specifier masks used by phase dispatch.
    // =========================================================================================
    // Block download initiate mask (ccs=6, cs=0): bits 5..7 = 110, bit 0 = 0.
    private const byte CcsBlockDownloadInitMask = 0xC0;

    // Block upload initiate mask (ccs=5, cs=00): bits 5..7 = 101, bits 0..1 = 00.
    private const byte CcsBlockUploadInitMask = 0xA0;

    // Block download end mask (ccs=6, cs=1): bits 5..7 = 110, bits 0..1 = 01.
    private const byte CcsBlockDownloadEndMask = 0xC1;

    // Server-side block-download initiate response mask (scs=5, ss=0): bits 5..7 = 101, bits 0..1 = 00.
    private const byte ScsInitResponseMaskDownload = 0xA0;

    // Server-side block-upload initiate response mask (scs=6, ss=0): bits 5..7 = 110, bits 0..1 = 00.
    private const byte ScsInitResponseMaskUpload = 0xC0;

    // Server-side block-upload end mask (scs=6, ss=1): bits 5..7 = 110, bits 0..1 = 01.
    private const byte ScsBlockUploadEndMask = 0xC1;

    // Server-side block-download sub-block ack (scs=5, ss=2): 0xA2.
    private const byte ScsBlockDownloadSubBlockAck = 0xA2;

    // Server-side block-download end response (scs=5, ss=1): 0xA1.
    private const byte ScsBlockDownloadEndResponse = 0xA1;

    // =========================================================================================
    // Session state objects (private nested).
    // =========================================================================================

    private enum SdoBlockClientPhase
    {
        AwaitInitResponse = 0,
        SendingSegments = 1,
        AwaitSubBlockAck = 2,
        AwaitEndResponse = 3,
        ReceivingSegments = 4,
        AwaitEnd = 5,
    }

    private enum SdoBlockServerPhase
    {
        AwaitInit = 0,
        ReceivingSegments = 1,
        AwaitEnd = 2,
        SendingSegments = 3,
        AwaitSubBlockAck = 4,
        AwaitEndResponse = 5,
        AwaitStart = 6,
    }

    private sealed class SdoBlockClientSession
    {
        public SdoBlockClientSession(byte serverNodeId, ushort index, byte subindex,
            bool isDownload, byte[]? payload, TaskCompletionSource<byte[]> tcs, bool localCrcSupported)
        {
            ServerNodeId = serverNodeId;
            Index = index;
            Subindex = subindex;
            IsDownload = isDownload;
            Payload = payload;
            Tcs = tcs;
            LocalCrcSupported = localCrcSupported;
        }

        public byte ServerNodeId { get; }
        public ushort Index { get; }
        public byte Subindex { get; }
        public bool IsDownload { get; }
        public byte[]? Payload { get; set; }
        public TaskCompletionSource<byte[]> Tcs { get; }
        public bool LocalCrcSupported { get; }
        public bool CrcActive { get; set; }
        public SdoBlockClientPhase Phase { get; set; } = SdoBlockClientPhase.AwaitInitResponse;

        // Download progress.
        public int Offset;
        public byte NegotiatedBlockSize;
        public byte SegmentsInFlight;
        public byte LastSegmentUnusedBytes;

        // Upload progress.
        public byte LocalBlockSize = 127;
        public byte NextExpectedSeq = 1;
        public uint DeclaredTotalSize;

        public IDeadline? Deadline;
    }

    private sealed class SdoBlockServerSession
    {
        public SdoBlockServerSession(bool inDownload, ushort index, byte subindex, byte[] buffer, int offset)
        {
            InDownload = inDownload;
            Index = index;
            Subindex = subindex;
            Buffer = buffer;
            Offset = offset;
        }

        public bool InDownload { get; }
        public ushort Index { get; }
        public byte Subindex { get; }
        public byte[] Buffer { get; set; }
        public int Offset { get; set; }
        public SdoBlockServerPhase Phase { get; set; }
        public byte NegotiatedBlockSize { get; set; }
        public byte NextExpectedSeq { get; set; }
        public byte SegmentsInSubBlock { get; set; }
        public byte LastSegmentUnusedBytes { get; set; }
        public bool CrcActive { get; set; }
        public uint DeclaredTotalSize { get; set; }
        public bool SizeIndicated { get; set; }
        public IDeadline? Deadline { get; set; }
    }
}
