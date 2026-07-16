using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.Addressing;
using CanKit.Pro.J1939Tp;
using CanKit.Pro.RawCan;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Virtual-loopback integration tests for the J1939-21 §5.10 Transport Protocol implementation
/// in <c>CanKit.Pro.J1939Tp</c> (SRS FR-TP-030..035). Uses the same
/// <c>CanKit.Adapter.Virtual</c> pattern the other L2/L3 tests use, so a real bus is never
/// required and the tests are portable across every CI runner.
/// </summary>
public class J1939TpTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    private static string NewSession() => $"j1939tp-{Guid.NewGuid():N}";

    // Both nodes share the same virtual bus (session-scoped) but appear as different channels of
    // the same VirtualBusHub, so a Transmit on one is seen by the other's FrameObserved. Every
    // test pins its own session to guarantee isolation.
    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    private static byte[] RandomPayload(int length, int seed)
    {
        var rng = new Random(seed);
        var buf = new byte[length];
        rng.NextBytes(buf);
        return buf;
    }

    // FR-TP-030 + FR-TP-032 + FR-TP-033: TP.BAM sender broadcasts a 100-byte PDU; the receiver
    // reassembles it identically from TP.CM(BAM) + TP.DT 1..15.
    [Fact]
    public async Task Bam_Roundtrip_ReceiverReassemblesIdenticalPayload()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        // Shorten Th so the test runs in <1s while still exercising the timer.
        var opts = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(5));

        using var sender = J1939Tp.Open(busA, sourceAddress: 0x11, options: opts);
        using var receiver = J1939Tp.Open(busB, sourceAddress: 0x22, options: opts);

        var payload = RandomPayload(100, seed: 42);
        var pgn = 0xFECAu; // arbitrary PDU2 broadcast PGN

        var receiveTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);
        await sender.SendBamAsync(pgn, payload).WithTimeout(ShortTimeout);

        var datagram = await receiveTask;
        datagram.Kind.Should().Be(J1939TpKind.Bam);
        datagram.SourceAddress.Should().Be(0x11);
        datagram.DestinationAddress.Should().Be(0xFF);
        datagram.Pgn.Should().Be(pgn);
        datagram.Payload.Should().Equal(payload);
    }

    // FR-TP-031 + FR-TP-032 + FR-TP-033: TP.CM sender emits RTS -> receiver replies CTS ->
    // sender streams TP.DT -> receiver reassembles and sends EndOfMsgAck -> sender's task
    // completes.
    [Fact]
    public async Task Cm_Roundtrip_ReceiverReassemblesAndAcks()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var sender = J1939Tp.Open(busA, sourceAddress: 0x01);
        using var receiver = J1939Tp.Open(busB, sourceAddress: 0x02);

        var payload = RandomPayload(300, seed: 7);
        var pgn = 0xEF00u; // arbitrary PDU1 destination-addressed PGN

        var receiveTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);
        await sender.SendCmAsync(pgn, destinationAddress: 0x02, payload).WithTimeout(ShortTimeout);

        var datagram = await receiveTask;
        datagram.Kind.Should().Be(J1939TpKind.Cm);
        datagram.SourceAddress.Should().Be(0x01);
        datagram.DestinationAddress.Should().Be(0x02);
        datagram.Pgn.Should().Be(pgn);
        datagram.Payload.Should().Equal(payload);
    }

    // FR-TP-032 + FR-TP-033: exact 7*N boundary payload -- one full block of TP.DT frames --
    // reassembles correctly.
    [Fact]
    public async Task Cm_ExactBoundaryPayload_Reassembles()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var sender = J1939Tp.Open(busA, sourceAddress: 0x03);
        using var receiver = J1939Tp.Open(busB, sourceAddress: 0x04);

        // 7 * 16 = 112 bytes -- one full CTS block at default MaxPacketsPerCts=16.
        var payload = RandomPayload(112, seed: 99);
        var receiveTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);

        await sender.SendCmAsync(0xEF10, destinationAddress: 0x04, payload).WithTimeout(ShortTimeout);
        var datagram = await receiveTask;
        datagram.Payload.Should().Equal(payload);
    }

    // FR-TP-034/035: two independent TP.CM sessions to different destinations run in parallel
    // over the same physical bus, plus a concurrent TP.BAM broadcast, all reassembled correctly
    // by the intended recipients.
    [Fact]
    public async Task Parallel_Bam_And_TwoCm_Sessions_Do_Not_Interfere()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var busC = Open(session, 2);

        using var sender = J1939Tp.Open(busA, sourceAddress: 0x10);
        using var receiverB = J1939Tp.Open(busB, sourceAddress: 0xB0);
        using var receiverC = J1939Tp.Open(busC, sourceAddress: 0xC0);

        var payloadBam = RandomPayload(200, seed: 1);
        var payloadCmB = RandomPayload(400, seed: 2);
        var payloadCmC = RandomPayload(250, seed: 3);

        var pgnBam = 0xFEF0u;
        var pgnCmB = 0xEE10u;
        var pgnCmC = 0xEE20u;

        // Collect BAM on both receivers, CM only on its target.
        var collectB = CollectAsync(receiverB, count: 2, ShortTimeout);
        var collectC = CollectAsync(receiverC, count: 2, ShortTimeout);

        var t1 = sender.SendBamAsync(pgnBam, payloadBam);
        var t2 = sender.SendCmAsync(pgnCmB, destinationAddress: 0xB0, payloadCmB);
        var t3 = sender.SendCmAsync(pgnCmC, destinationAddress: 0xC0, payloadCmC);
        await Task.WhenAll(t1, t2, t3).WithTimeout(ShortTimeout);

        var listB = await collectB;
        var listC = await collectC;

        // ReceiverB must see the BAM (broadcast) and its own CM.
        listB.Should().HaveCount(2);
        var bamOnB = listB.Single(d => d.Kind == J1939TpKind.Bam);
        var cmOnB = listB.Single(d => d.Kind == J1939TpKind.Cm);
        bamOnB.Pgn.Should().Be(pgnBam);
        bamOnB.Payload.Should().Equal(payloadBam);
        cmOnB.Pgn.Should().Be(pgnCmB);
        cmOnB.Payload.Should().Equal(payloadCmB);

        // ReceiverC must see the BAM and its own CM (not the CM sent to B).
        listC.Should().HaveCount(2);
        var bamOnC = listC.Single(d => d.Kind == J1939TpKind.Bam);
        var cmOnC = listC.Single(d => d.Kind == J1939TpKind.Cm);
        bamOnC.Pgn.Should().Be(pgnBam);
        bamOnC.Payload.Should().Equal(payloadBam);
        cmOnC.Pgn.Should().Be(pgnCmC);
        cmOnC.Payload.Should().Equal(payloadCmC);
    }

    // FR-TP-032 negative path: with no peer to answer, the TP.CM sender's own T3 timer expires
    // and its SendCmAsync task faults with a J1939TpAbortException(Reason=Timeout).
    [Fact]
    public async Task Cm_NoPeer_TimesOutWithAbortException()
    {
        var session = NewSession();
        using var bus = Open(session, 0);

        // Only the sender is present; nobody will reply with CTS. Shorten T3 so the test runs
        // in ~200 ms instead of the standard-recommended 1250 ms.
        var opts = new J1939TpOptions().With(
            t2: TimeSpan.FromMilliseconds(150),
            t3: TimeSpan.FromMilliseconds(150));

        using var sender = J1939Tp.Open(bus, sourceAddress: 0x30, options: opts);

        var send = sender.SendCmAsync(0xEE30, destinationAddress: 0x99,
            RandomPayload(50, seed: 5));

        Func<Task> act = async () => await send.WithTimeout(ShortTimeout);
        var ex = (await act.Should().ThrowAsync<J1939TpAbortException>()).Which;
        ex.Reason.Should().Be(J1939TpAbortReason.Timeout);
        ex.Pgn.Should().Be(0xEE30u);
    }

    // FR-TP-030 lower-bound check: a 9-byte payload (the smallest legal J1939-TP payload;
    // anything ≤ 8 must use single-frame per §5.10.1) still round-trips correctly.
    [Fact]
    public async Task Bam_MinimumPayload_Roundtrip()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        var opts = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(5));
        using var sender = J1939Tp.Open(busA, sourceAddress: 0x40, options: opts);
        using var receiver = J1939Tp.Open(busB, sourceAddress: 0x41, options: opts);

        var payload = RandomPayload(9, seed: 11);
        var receiveTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);
        await sender.SendBamAsync(0xFEEE, payload).WithTimeout(ShortTimeout);

        var datagram = await receiveTask;
        datagram.Payload.Should().Equal(payload);
    }

    // FR-TP-030 upper-bound check: the largest legal J1939-TP payload (1785 bytes = 255 * 7)
    // still round-trips via BAM. Exercises SN wrap all the way to 255 without off-by-one bugs.
    [Fact]
    public async Task Bam_MaximumPayload_Roundtrip()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        // Th=1ms so 255 hold-offs together take ~0.25s rather than dominating test time; the
        // Virtual hub delivers synchronously so timing is not what we're checking here.
        var opts = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(1));
        using var sender = J1939Tp.Open(busA, sourceAddress: 0x50, options: opts);
        using var receiver = J1939Tp.Open(busB, sourceAddress: 0x51, options: opts);

        var payload = RandomPayload(1785, seed: 13);
        var receiveTask = receiver.ReceiveAsync().AsTaskWithTimeout(TimeSpan.FromSeconds(10));
        await sender.SendBamAsync(0xFEED, payload).WithTimeout(TimeSpan.FromSeconds(10));

        var datagram = await receiveTask;
        datagram.Payload.Should().Equal(payload);
    }

    // Bugbot 3595010737 (PR #25): TP.DT frames carry no PGN, so an RX-session map keyed by
    // (SA, PGN) forces the DT handler to guess a session by (SA, kind) alone, which corrupts
    // reassembly if two overlapping CM sessions from the same source (different PGNs) coexist.
    // J1939-21 §5.10.3 requires exactly one CM connection per (SA, DA) pair, so the fix is to
    // refuse the second RTS with SessionAlreadyOpen and keep the first session's DT stream
    // running to completion. This test replays that scenario end-to-end via raw frame injection.
    [Fact]
    public async Task SecondRtsFromSamePeer_DifferentPgn_IsAbortedAndDtRoutesToActiveSession()
    {
        var session = NewSession();
        using var receiverBus = Open(session, 0);
        using var peerBus = Open(session, 1);

        const byte receiverSa = 0x22;
        const byte peerSa = 0x11;
        const uint activePgn = 0xABCDu;
        const uint intruderPgn = 0x9876u;

        // 14-byte payload = exactly 2 TP.DT frames -> minimal, deterministic size.
        var payload = RandomPayload(14, seed: 314);

        using var receiver = J1939Tp.Open(receiverBus, sourceAddress: receiverSa);

        // Observe every TP.CM frame the receiver emits so we can inspect CTS / EOM / Abort.
        var observed = new List<(uint canId, byte[] data)>();
        var cmFrames = new List<(uint canId, byte[] data)>();
        var frameReady = new SemaphoreSlim(0);
        peerBus.FrameObserved += (_, e) =>
        {
            var frame = e.CanFrame;
            if (!frame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)frame.ID);
            if (fields.SourceAddress != receiverSa) return; // only interested in what the SUT emits
            var data = frame.Data.ToArray();
            lock (observed)
            {
                observed.Add(((uint)frame.ID, data));
                if (J1939Pgn.IsTransportCm(fields.Pgn))
                    cmFrames.Add(((uint)frame.ID, data));
            }
            frameReady.Release();
        };

        async Task<byte[]> WaitForCmFrameAsync(Func<byte[], bool> predicate, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            while (true)
            {
                lock (cmFrames)
                {
                    foreach (var (_, data) in cmFrames)
                        if (predicate(data)) return data;
                }
                await frameReady.WaitAsync(cts.Token).ConfigureAwait(false);
            }
        }

        static byte[] BuildFrame(byte[] payload) => payload; // clarity alias

        // --- 1. Send RTS #1 from peer for activePgn (2 packets, 14 bytes) ---
        var rts1 = J1939TpFrames.BuildRts(totalBytes: 14, totalPackets: 2, maxPacketsPerCts: 0xFF, dataPgn: activePgn);
        var rts1Id = J1939Id.ComposePgn(priority: 7, pgn: J1939Pgn.TpCm, sourceAddress: peerSa, destinationAddress: receiverSa);
        peerBus.Transmit(CanFrame.Classic((int)rts1Id, BuildFrame(rts1), isExtendedFrame: true));

        // --- 2. Wait for CTS from the receiver for activePgn ---
        var cts1 = await WaitForCmFrameAsync(
            d => d.Length >= 8 && d[0] == J1939TpFrames.ControlCts
                 && J1939TpFrames.ReadDataPgn(d) == activePgn,
            ShortTimeout);
        cts1[1].Should().BeGreaterThan(0, "receiver must grant at least one packet");
        cts1[2].Should().Be(1, "next expected SN is 1");

        // --- 3. Inject RTS #2 from *same* peer SA but for a *different* PGN ---
        var rts2 = J1939TpFrames.BuildRts(totalBytes: 14, totalPackets: 2, maxPacketsPerCts: 0xFF, dataPgn: intruderPgn);
        peerBus.Transmit(CanFrame.Classic((int)rts1Id, BuildFrame(rts2), isExtendedFrame: true));

        // --- 4. Assert receiver refuses the intruder with SessionAlreadyOpen tagged with the
        //         intruder's PGN, without disturbing the active session ---
        var abort = await WaitForCmFrameAsync(
            d => d.Length >= 8 && d[0] == J1939TpFrames.ControlAbort
                 && J1939TpFrames.ReadDataPgn(d) == intruderPgn,
            ShortTimeout);
        abort[1].Should().Be((byte)J1939TpAbortReason.SessionAlreadyOpen);

        // Receiver must not have started tearing down / re-CTSing the active session.
        lock (cmFrames)
        {
            cmFrames.Should().NotContain(t =>
                t.data[0] == J1939TpFrames.ControlAbort && J1939TpFrames.ReadDataPgn(t.data) == activePgn,
                "the active session must remain untouched by the rejected intruder");
        }

        // --- 5. Send the two TP.DT frames for the *active* session and verify they route
        //         correctly (and not to the just-rejected intruder). ---
        var dtId = J1939Id.ComposePgn(priority: 7, pgn: J1939Pgn.TpDt, sourceAddress: peerSa, destinationAddress: receiverSa);
        var dt1 = J1939TpFrames.BuildDt(sn: 1, pdu: payload, offset: 0);
        var dt2 = J1939TpFrames.BuildDt(sn: 2, pdu: payload, offset: 7);
        peerBus.Transmit(CanFrame.Classic((int)dtId, BuildFrame(dt1), isExtendedFrame: true));
        peerBus.Transmit(CanFrame.Classic((int)dtId, BuildFrame(dt2), isExtendedFrame: true));

        // --- 6. Receiver must produce an EndOfMsgAck for the *active* PGN and hand up the PDU ---
        var eom = await WaitForCmFrameAsync(
            d => d.Length >= 8 && d[0] == J1939TpFrames.ControlEomAck
                 && J1939TpFrames.ReadDataPgn(d) == activePgn,
            ShortTimeout);
        (eom[1] | (eom[2] << 8)).Should().Be(14);
        eom[3].Should().Be(2);

        var datagram = await receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);
        datagram.Kind.Should().Be(J1939TpKind.Cm);
        datagram.SourceAddress.Should().Be(peerSa);
        datagram.DestinationAddress.Should().Be(receiverSa);
        datagram.Pgn.Should().Be(activePgn);
        datagram.Payload.Should().Equal(payload);
    }

    // Companion coverage for the DT-routing invariant: two peers sending concurrent CM sessions
    // for *different* PGNs each get their DTs routed to *their own* session, even though the
    // second peer's PGN differs from the first peer's PGN. This directly exercises the
    // (SA, kind)-keyed rxSessions map (pre-fix, the DT handler picked the first (SA,*) match --
    // which happened to work by accident when only one peer is active, but broke reassembly
    // for concurrent transfers).
    [Fact]
    public async Task TwoPeers_ConcurrentCm_DifferentPgns_EachReassembledByPeerSa()
    {
        var session = NewSession();
        using var receiverBus = Open(session, 0);
        using var peerABus = Open(session, 1);
        using var peerBBus = Open(session, 2);

        const byte receiverSa = 0x22;
        const byte peerASa = 0x11;
        const byte peerBSa = 0x33;
        const uint pgnA = 0xEE10u;
        const uint pgnB = 0xEE20u;

        var payloadA = RandomPayload(14, seed: 1);
        var payloadB = RandomPayload(14, seed: 2);

        using var receiver = J1939Tp.Open(receiverBus, sourceAddress: receiverSa);

        // Interleave DTs from the two peers to force the (SA, kind) routing to demux correctly.
        async Task DriveAsync(ICanBus bus, byte peerSa, uint pgn, byte[] pdu)
        {
            var rts = J1939TpFrames.BuildRts(pdu.Length, J1939TpFrames.TotalPackets(pdu.Length), 0xFF, pgn);
            var cmId = J1939Id.ComposePgn(7, J1939Pgn.TpCm, peerSa, receiverSa);
            var dtId = J1939Id.ComposePgn(7, J1939Pgn.TpDt, peerSa, receiverSa);
            bus.Transmit(CanFrame.Classic((int)cmId, rts, isExtendedFrame: true));
            // Small gap so both RTSs land before either DT stream begins.
            await Task.Delay(20).ConfigureAwait(false);
            for (byte sn = 1; sn <= J1939TpFrames.TotalPackets(pdu.Length); sn++)
            {
                var dt = J1939TpFrames.BuildDt(sn, pdu, (sn - 1) * J1939TpFrames.DtDataBytes);
                bus.Transmit(CanFrame.Classic((int)dtId, dt, isExtendedFrame: true));
                await Task.Delay(5).ConfigureAwait(false);
            }
        }

        var driveA = DriveAsync(peerABus, peerASa, pgnA, payloadA);
        var driveB = DriveAsync(peerBBus, peerBSa, pgnB, payloadB);
        await Task.WhenAll(driveA, driveB).WithTimeout(ShortTimeout);

        var received = await CollectAsync(receiver, count: 2, ShortTimeout);
        received.Should().HaveCount(2);
        var fromA = received.Single(d => d.SourceAddress == peerASa);
        var fromB = received.Single(d => d.SourceAddress == peerBSa);
        fromA.Pgn.Should().Be(pgnA);
        fromA.Payload.Should().Equal(payloadA);
        fromB.Pgn.Should().Be(pgnB);
        fromB.Payload.Should().Equal(payloadB);
    }

    // Bugbot 3596183535: BAM announce TX rejection must fail SendBamAsync (not only raise
    // BackgroundExceptionOccurred) and must not proceed to TP.DT after Th.
    [Fact]
    public async Task Bam_AnnounceTxRejected_FailsSendAndDoesNotEmitDt()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var inner = new CanBusService(busA);
        using var rejecting = new RejectTpCmBusService(inner);
        var opts = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(5));
        using var sender = J1939Tp.Open(rejecting, sourceAddress: 0x51, options: opts, leaveOpen: true);

        var dtSeen = 0;
        busB.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.SourceAddress == 0x51 && J1939Pgn.IsTransportDt(fields.Pgn))
                Interlocked.Increment(ref dtSeen);
        };

        var bgSeen = 0;
        sender.BackgroundExceptionOccurred += (_, _) => Interlocked.Increment(ref bgSeen);

        Func<Task> act = async () => await sender.SendBamAsync(0xFE51, RandomPayload(20, seed: 51))
            .WithTimeout(ShortTimeout);
        await act.Should().ThrowAsync<J1939TpSendRejectedException>();

        // Give Th a chance to fire if DT were incorrectly scheduled after a rejected BAM.
        await Task.Delay(80);
        Volatile.Read(ref dtSeen).Should().Be(0, "rejected BAM announce must not schedule TP.DT");
        Volatile.Read(ref bgSeen).Should().Be(0, "CM TX failure must fail the send TCS, not only BackgroundExceptionOccurred");
    }

    // Bugbot 3596025915: canceling before BeginTxOnLoop runs must not emit TP.CM/TP.DT.
    [Fact]
    public async Task SendCm_CanceledBeforeStart_DoesNotTransmit()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var sender = J1939Tp.Open(busA, sourceAddress: 0x61);
        using var _ = J1939Tp.Open(busB, sourceAddress: 0x62);

        var seen = 0;
        busB.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.SourceAddress == 0x61 &&
                (J1939Pgn.IsTransportCm(fields.Pgn) || J1939Pgn.IsTransportDt(fields.Pgn)))
                Interlocked.Increment(ref seen);
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await sender.SendCmAsync(0xEE61, destinationAddress: 0x62,
            RandomPayload(50, seed: 61), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Give the actor a beat to drain any incorrectly queued BeginTx work.
        await Task.Delay(100);
        Volatile.Read(ref seen).Should().Be(0, "canceled send must not emit TP.CM/TP.DT");
    }

    // Bugbot 3596025922: canceling an in-flight TP.CM after RTS must send Connection Abort.
    [Fact]
    public async Task SendCm_CancelInFlight_SendsConnectionAbort()
    {
        var session = NewSession();
        using var senderBus = Open(session, 0);
        using var peerBus = Open(session, 1);

        const byte senderSa = 0x71;
        const byte peerSa = 0x72;
        const uint pgn = 0xEE71u;

        using var sender = J1939Tp.Open(senderBus, sourceAddress: senderSa);

        var abortSeen = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        peerBus.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.SourceAddress != senderSa || !J1939Pgn.IsTransportCm(fields.Pgn)) return;
            var data = e.CanFrame.Data.ToArray();
            if (data.Length >= 8 && data[0] == J1939TpFrames.ControlAbort
                && J1939TpFrames.ReadDataPgn(data) == pgn)
                abortSeen.TrySetResult(data);
        };

        // Peer never replies with CTS, so the session stays open after RTS until we cancel.
        using var cts = new CancellationTokenSource();
        var send = sender.SendCmAsync(pgn, destinationAddress: peerSa, RandomPayload(50, seed: 71), cts.Token);

        // Wait until RTS has hit the wire (actor has registered the TX session).
        await Task.Delay(50);
        cts.Cancel();

        Func<Task> act = async () => await send.WithTimeout(ShortTimeout);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var abort = await abortSeen.Task.AsTaskWithTimeout(ShortTimeout);
        abort[1].Should().Be((byte)J1939TpAbortReason.NoResourcesAvailable);
    }

    // Bugbot 3596025929: MaxPacketsPerCts=0 must fail fast, not crash later in BuildCts.
    [Fact]
    public void Open_MaxPacketsPerCtsZero_Throws()
    {
        var session = NewSession();
        using var bus = Open(session, 0);
        var opts = new J1939TpOptions { MaxPacketsPerCts = 0 };

        Action act = () => J1939Tp.Open(bus, sourceAddress: 0x81, options: opts);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(nameof(J1939TpOptions.MaxPacketsPerCts));
    }

    [Fact]
    public void Options_With_MaxPacketsPerCtsZero_Throws()
    {
        Action act = () => new J1939TpOptions().With(maxPacketsPerCts: 0);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("maxPacketsPerCts");
    }

    // Bugbot 3596025934 / 3596396508: Tr (CTS → first DT) must abort on the wire, raise
    // BackgroundExceptionOccurred, and fault a blocked ReceiveAsync (IsoTp AbortRx pattern).
    [Fact]
    public async Task Cm_Receiver_TrTimeout_AbortsWhenNoDtAfterCts()
    {
        var session = NewSession();
        using var receiverBus = Open(session, 0);
        using var peerBus = Open(session, 1);

        const byte receiverSa = 0x82;
        const byte peerSa = 0x83;
        const uint pgn = 0xEE82u;

        var opts = new J1939TpOptions().With(
            tr: TimeSpan.FromMilliseconds(80),
            t1: TimeSpan.FromSeconds(5)); // T1 must not fire first

        using var receiver = J1939Tp.Open(receiverBus, sourceAddress: receiverSa, options: opts);

        var abortSeen = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bgAbort = new TaskCompletionSource<J1939TpAbortException>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.BackgroundExceptionOccurred += (_, ex) =>
        {
            if (ex is J1939TpAbortException abort) bgAbort.TrySetResult(abort);
        };
        peerBus.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.SourceAddress != receiverSa || !J1939Pgn.IsTransportCm(fields.Pgn)) return;
            var data = e.CanFrame.Data.ToArray();
            if (data.Length >= 8 && data[0] == J1939TpFrames.ControlAbort
                && J1939TpFrames.ReadDataPgn(data) == pgn)
                abortSeen.TrySetResult(data);
        };

        var recvTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);

        var rts = J1939TpFrames.BuildRts(totalBytes: 14, totalPackets: 2, maxPacketsPerCts: 0xFF, dataPgn: pgn);
        var rtsId = J1939Id.ComposePgn(7, J1939Pgn.TpCm, peerSa, receiverSa);
        peerBus.Transmit(CanFrame.Classic((int)rtsId, rts, isExtendedFrame: true));
        // Do not send any TP.DT — Tr must expire and abort.

        var abortFrame = await abortSeen.Task.AsTaskWithTimeout(ShortTimeout);
        abortFrame[1].Should().Be((byte)J1939TpAbortReason.Timeout);

        Func<Task> act = () => recvTask;
        var recvEx = (await act.Should().ThrowAsync<J1939TpAbortException>()).Which;
        recvEx.Reason.Should().Be(J1939TpAbortReason.Timeout);
        recvEx.Message.Should().Contain("Tr");

        var ex = await bgAbort.Task.AsTaskWithTimeout(ShortTimeout);
        ex.Reason.Should().Be(J1939TpAbortReason.Timeout);
        ex.Message.Should().Contain("Tr");
    }

    // Bugbot 3596396508: mismatched TP.DT SN must AbortRx — fault blocked ReceiveAsync and raise
    // BackgroundExceptionOccurred (wire Abort for CM). Channel stays usable afterward.
    [Fact]
    public async Task Cm_Receiver_BadDtSequence_FaultsReceiveAsync()
    {
        var session = NewSession();
        using var receiverBus = Open(session, 0);
        using var peerBus = Open(session, 1);

        const byte receiverSa = 0x84;
        const byte peerSa = 0x85;
        const uint pgn = 0xEE84u;
        var payload = RandomPayload(14, seed: 99);

        using var receiver = J1939Tp.Open(receiverBus, sourceAddress: receiverSa);

        var ctsSeen = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var abortSeen = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bgAbort = new TaskCompletionSource<J1939TpAbortException>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.BackgroundExceptionOccurred += (_, ex) =>
        {
            if (ex is J1939TpAbortException abort) bgAbort.TrySetResult(abort);
        };
        peerBus.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.SourceAddress != receiverSa || !J1939Pgn.IsTransportCm(fields.Pgn)) return;
            var data = e.CanFrame.Data.ToArray();
            if (data.Length < 8 || J1939TpFrames.ReadDataPgn(data) != pgn) return;
            if (data[0] == J1939TpFrames.ControlCts) ctsSeen.TrySetResult(data);
            else if (data[0] == J1939TpFrames.ControlAbort) abortSeen.TrySetResult(data);
        };

        var recvTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);

        var rts = J1939TpFrames.BuildRts(totalBytes: 14, totalPackets: 2, maxPacketsPerCts: 0xFF, dataPgn: pgn);
        var rtsId = J1939Id.ComposePgn(7, J1939Pgn.TpCm, peerSa, receiverSa);
        peerBus.Transmit(CanFrame.Classic((int)rtsId, rts, isExtendedFrame: true));

        await ctsSeen.Task.AsTaskWithTimeout(ShortTimeout);

        // Inject SN=2 while SN=1 was expected.
        var dtId = J1939Id.ComposePgn(7, J1939Pgn.TpDt, peerSa, receiverSa);
        var badDt = J1939TpFrames.BuildDt(sn: 2, pdu: payload, offset: 7);
        peerBus.Transmit(CanFrame.Classic((int)dtId, badDt, isExtendedFrame: true));

        var abortFrame = await abortSeen.Task.AsTaskWithTimeout(ShortTimeout);
        abortFrame[1].Should().Be((byte)J1939TpAbortReason.UnexpectedCtsSequenceNumber);

        Func<Task> act = () => recvTask;
        var recvEx = (await act.Should().ThrowAsync<J1939TpAbortException>()).Which;
        recvEx.Reason.Should().Be(J1939TpAbortReason.UnexpectedCtsSequenceNumber);
        recvEx.Pgn.Should().Be(pgn);
        recvEx.Message.Should().Contain("unexpected TP.DT sequence number");

        var bgEx = await bgAbort.Task.AsTaskWithTimeout(ShortTimeout);
        bgEx.Reason.Should().Be(J1939TpAbortReason.UnexpectedCtsSequenceNumber);

        // Channel remains usable for a subsequent BAM after the abort (fault consumed once).
        var opts = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(5));
        using var senderBus = Open(session, 2);
        using var sender = J1939Tp.Open(senderBus, sourceAddress: 0x11, options: opts);
        var okPayload = RandomPayload(14, seed: 123);
        var recv2 = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);
        await sender.SendBamAsync(0xFECAu, okPayload).WithTimeout(ShortTimeout);
        var datagram = await recv2;
        datagram.Kind.Should().Be(J1939TpKind.Bam);
        datagram.Payload.Should().Equal(okPayload);
    }

    // Bugbot 3596475712: ReadDataPgn must mask reserved bits in TP.CM byte 7 (18-bit PGN).
    // Without the mask, BuildCts throws after the RX session is registered and ArmTr never runs,
    // leaving a timerless orphan that blocks further CM from that source (§5.10.3).
    [Fact]
    public void ReadDataPgn_MasksReservedBitsInByte7()
    {
        const uint pgn = 0x12345u; // fits in 18 bits
        var rts = J1939TpFrames.BuildRts(totalBytes: 14, totalPackets: 2, maxPacketsPerCts: 0xFF, dataPgn: pgn);
        rts[7] |= 0xFC; // set reserved upper 6 bits (would yield > MaxValue if unmasked)

        J1939TpFrames.ReadDataPgn(rts).Should().Be(pgn);
        Action act = () => J1939TpFrames.BuildCts(numPackets: 1, nextPacketSn: 1, dataPgn: J1939TpFrames.ReadDataPgn(rts));
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Cm_Receiver_RtsWithReservedPgnBits_RepliesCtsAndArmsTr()
    {
        var session = NewSession();
        using var receiverBus = Open(session, 0);
        using var peerBus = Open(session, 1);

        const byte receiverSa = 0x88;
        const byte peerSa = 0x89;
        const uint pgn = 0xEE88u;

        var opts = new J1939TpOptions().With(
            tr: TimeSpan.FromMilliseconds(80),
            t1: TimeSpan.FromSeconds(5));

        using var receiver = J1939Tp.Open(receiverBus, sourceAddress: receiverSa, options: opts);

        var ctsSeen = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var abortSeen = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        peerBus.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.SourceAddress != receiverSa || !J1939Pgn.IsTransportCm(fields.Pgn)) return;
            var data = e.CanFrame.Data.ToArray();
            if (data.Length < 8 || J1939TpFrames.ReadDataPgn(data) != pgn) return;
            if (data[0] == J1939TpFrames.ControlCts) ctsSeen.TrySetResult(data);
            else if (data[0] == J1939TpFrames.ControlAbort) abortSeen.TrySetResult(data);
        };

        var rts = J1939TpFrames.BuildRts(totalBytes: 14, totalPackets: 2, maxPacketsPerCts: 0xFF, dataPgn: pgn);
        rts[7] |= 0xFC; // reserved bits set — must not orphan the CM RX session
        var rtsId = J1939Id.ComposePgn(7, J1939Pgn.TpCm, peerSa, receiverSa);
        peerBus.Transmit(CanFrame.Classic((int)rtsId, rts, isExtendedFrame: true));

        var cts = await ctsSeen.Task.AsTaskWithTimeout(ShortTimeout);
        J1939TpFrames.ReadDataPgn(cts).Should().Be(pgn);

        // Tr must be armed: with no DT, the receiver aborts for timeout (not a timerless orphan).
        var abortFrame = await abortSeen.Task.AsTaskWithTimeout(ShortTimeout);
        abortFrame[1].Should().Be((byte)J1939TpAbortReason.Timeout);
    }

    // Bugbot 3596396508 (BAM path): bad SN must fault a blocked ReceiveAsync (not only raise
    // BackgroundExceptionOccurred). Cancel() disposes T1, so inbox fault is the unblock path.
    [Fact]
    public async Task Bam_Receiver_BadDtSequence_FaultsReceiveAsync()
    {
        var session = NewSession();
        using var receiverBus = Open(session, 0);
        using var peerBus = Open(session, 1);

        const byte receiverSa = 0x86;
        const byte peerSa = 0x87;
        const uint pgn = 0xFECBu;
        var payload = RandomPayload(14, seed: 100);

        // Long T1 so a hang would outlive the test timeout if we failed to notify.
        var opts = new J1939TpOptions().With(t1: TimeSpan.FromSeconds(30));
        using var receiver = J1939Tp.Open(receiverBus, sourceAddress: receiverSa, options: opts);

        var bgAbort = new TaskCompletionSource<J1939TpAbortException>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.BackgroundExceptionOccurred += (_, ex) =>
        {
            if (ex is J1939TpAbortException abort) bgAbort.TrySetResult(abort);
        };

        var recvTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);

        var bam = J1939TpFrames.BuildBam(totalBytes: 14, totalPackets: 2, dataPgn: pgn);
        var bamId = J1939Id.ComposePgn(7, J1939Pgn.TpCm, peerSa, J1939Pgn.GlobalAddress);
        peerBus.Transmit(CanFrame.Classic((int)bamId, bam, isExtendedFrame: true));

        // Virtual hub delivers synchronously; BAM is armed before we inject the bad DT.
        var dtId = J1939Id.ComposePgn(7, J1939Pgn.TpDt, peerSa, J1939Pgn.GlobalAddress);
        var badDt = J1939TpFrames.BuildDt(sn: 2, pdu: payload, offset: 7);
        peerBus.Transmit(CanFrame.Classic((int)dtId, badDt, isExtendedFrame: true));

        Func<Task> act = () => recvTask;
        var recvEx = (await act.Should().ThrowAsync<J1939TpAbortException>()).Which;
        recvEx.Reason.Should().Be(J1939TpAbortReason.UnexpectedCtsSequenceNumber);
        recvEx.Pgn.Should().Be(pgn);
        recvEx.Message.Should().Contain("Bam");
        recvEx.Message.Should().Contain("unexpected TP.DT sequence number");

        var bgEx = await bgAbort.Task.AsTaskWithTimeout(ShortTimeout);
        bgEx.Reason.Should().Be(J1939TpAbortReason.UnexpectedCtsSequenceNumber);
    }

    // Bugbot 3596489078: a stray/early EndOfMsgAck while still waiting for CTS must abort
    // SendCmAsync — not complete it successfully before any DT has been sent.
    [Fact]
    public async Task Cm_Sender_PrematureEom_FailsSend()
    {
        var session = NewSession();
        using var senderBus = Open(session, 0);
        using var peerBus = Open(session, 1);

        const byte senderSa = 0x91;
        const byte peerSa = 0x92;
        const uint pgn = 0xEE91u;
        var payload = RandomPayload(14, seed: 91);

        using var sender = J1939Tp.Open(senderBus, sourceAddress: senderSa);

        var rtsSeen = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var abortSeen = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        peerBus.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.SourceAddress != senderSa || !J1939Pgn.IsTransportCm(fields.Pgn)) return;
            var data = e.CanFrame.Data.ToArray();
            if (data.Length < 8 || J1939TpFrames.ReadDataPgn(data) != pgn) return;
            if (data[0] == J1939TpFrames.ControlRts) rtsSeen.TrySetResult(data);
            else if (data[0] == J1939TpFrames.ControlAbort) abortSeen.TrySetResult(data);
        };

        var sendTask = sender.SendCmAsync(pgn, destinationAddress: peerSa, payload);

        await rtsSeen.Task.AsTaskWithTimeout(ShortTimeout);

        // Inject EOM before any CTS — sender is still in WaitCts.
        var eom = J1939TpFrames.BuildEomAck(payload.Length, J1939TpFrames.TotalPackets(payload.Length), pgn);
        var cmId = J1939Id.ComposePgn(7, J1939Pgn.TpCm, peerSa, senderSa);
        peerBus.Transmit(CanFrame.Classic((int)cmId, eom, isExtendedFrame: true));

        Func<Task> act = () => sendTask.WithTimeout(ShortTimeout);
        var ex = (await act.Should().ThrowAsync<J1939TpAbortException>()).Which;
        ex.Reason.Should().Be(J1939TpAbortReason.Unknown);
        ex.Message.Should().Contain("WaitEom");

        var abortFrame = await abortSeen.Task.AsTaskWithTimeout(ShortTimeout);
        abortFrame[1].Should().Be((byte)J1939TpAbortReason.Unknown);
    }

    // Bugbot 3596489078: EOM totals that disagree with the session must fail SendCmAsync
    // (not complete successfully with only a BackgroundExceptionOccurred).
    [Fact]
    public async Task Cm_Sender_EomSizeMismatch_FailsSend()
    {
        var session = NewSession();
        using var senderBus = Open(session, 0);
        using var peerBus = Open(session, 1);

        const byte senderSa = 0x93;
        const byte peerSa = 0x94;
        const uint pgn = 0xEE93u;
        var payload = RandomPayload(14, seed: 93); // 2 packets

        using var sender = J1939Tp.Open(senderBus, sourceAddress: senderSa);

        var rtsSeen = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastDtSeen = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
        peerBus.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.SourceAddress != senderSa) return;
            var data = e.CanFrame.Data.ToArray();
            if (J1939Pgn.IsTransportCm(fields.Pgn) && data.Length >= 8
                && data[0] == J1939TpFrames.ControlRts && J1939TpFrames.ReadDataPgn(data) == pgn)
                rtsSeen.TrySetResult(data);
            else if (J1939Pgn.IsTransportDt(fields.Pgn) && data.Length >= 1 && data[0] == 2)
                lastDtSeen.TrySetResult(data[0]);
        };

        var sendTask = sender.SendCmAsync(pgn, destinationAddress: peerSa, payload);

        await rtsSeen.Task.AsTaskWithTimeout(ShortTimeout);

        // Grant both packets so the sender reaches WaitEom after DT SN=2 is confirmed.
        var cts = J1939TpFrames.BuildCts(numPackets: 2, nextPacketSn: 1, dataPgn: pgn);
        var cmId = J1939Id.ComposePgn(7, J1939Pgn.TpCm, peerSa, senderSa);
        peerBus.Transmit(CanFrame.Classic((int)cmId, cts, isExtendedFrame: true));

        await lastDtSeen.Task.AsTaskWithTimeout(ShortTimeout);
        // Small settle so OnCmDtConfirmed arms WaitEom before we inject the bad EOM.
        await Task.Delay(20);

        var badEom = J1939TpFrames.BuildEomAck(payload.Length, J1939TpFrames.TotalPackets(payload.Length), pgn);
        badEom[1] = (byte)(payload.Length + 1); // mismatch totals vs session
        peerBus.Transmit(CanFrame.Classic((int)cmId, badEom, isExtendedFrame: true));

        Func<Task> act = () => sendTask.WithTimeout(ShortTimeout);
        var ex = (await act.Should().ThrowAsync<J1939TpAbortException>()).Which;
        ex.Reason.Should().Be(J1939TpAbortReason.Unknown);
        ex.Message.Should().Contain("EOM ack size mismatch");
    }

    // Bugbot 3596489082: an invalid BAM must not cancel an in-progress BAM from the same source
    // and leave ReceiveAsync hung with no session and no inbox fault.
    [Fact]
    public async Task Bam_Receiver_InvalidBamDoesNotSupersedeInProgress()
    {
        var session = NewSession();
        using var receiverBus = Open(session, 0);
        using var peerBus = Open(session, 1);

        const byte receiverSa = 0x95;
        const byte peerSa = 0x96;
        const uint pgn = 0xFECDu;
        var payload = RandomPayload(14, seed: 95);

        var opts = new J1939TpOptions().With(t1: TimeSpan.FromSeconds(5));
        using var receiver = J1939Tp.Open(receiverBus, sourceAddress: receiverSa, options: opts);

        var recvTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);

        var bamId = J1939Id.ComposePgn(7, J1939Pgn.TpCm, peerSa, J1939Pgn.GlobalAddress);
        var goodBam = J1939TpFrames.BuildBam(totalBytes: 14, totalPackets: 2, dataPgn: pgn);
        peerBus.Transmit(CanFrame.Classic((int)bamId, goodBam, isExtendedFrame: true));

        // Malformed BAM (bytes/packets disagree) — must not tear down the good session.
        var badBam = J1939TpFrames.BuildBam(totalBytes: 14, totalPackets: 2, dataPgn: pgn);
        badBam[3] = 3; // claim 3 packets for 14 bytes
        peerBus.Transmit(CanFrame.Classic((int)bamId, badBam, isExtendedFrame: true));

        var dtId = J1939Id.ComposePgn(7, J1939Pgn.TpDt, peerSa, J1939Pgn.GlobalAddress);
        peerBus.Transmit(CanFrame.Classic((int)dtId,
            J1939TpFrames.BuildDt(sn: 1, pdu: payload, offset: 0), isExtendedFrame: true));
        peerBus.Transmit(CanFrame.Classic((int)dtId,
            J1939TpFrames.BuildDt(sn: 2, pdu: payload, offset: 7), isExtendedFrame: true));

        var datagram = await recvTask;
        datagram.Kind.Should().Be(J1939TpKind.Bam);
        datagram.Pgn.Should().Be(pgn);
        datagram.Payload.Should().Equal(payload);
    }

    private static async Task<List<J1939TpDatagram>> CollectAsync(IJ1939TpChannel channel, int count, TimeSpan timeout)
    {
        var list = new List<J1939TpDatagram>();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var d in channel.ReceiveAllAsync(cts.Token))
            {
                list.Add(d);
                if (list.Count >= count) break;
            }
        }
        catch (OperationCanceledException)
        {
            // timeout -> return what we have
        }
        return list;
    }
}

/// <summary>
/// Test double: rejects every TP.CM frame at SendConfirmed, forwards everything else.
/// Used to prove BAM/RTS TX failure fails the send TCS (Bugbot 3596183535).
/// </summary>
internal sealed class RejectTpCmBusService : ICanBusService
{
    private readonly ICanBusService _inner;

    public RejectTpCmBusService(ICanBusService inner) => _inner = inner;

    public ICanBus Bus => _inner.Bus;
    public int SubscriptionCount => _inner.SubscriptionCount;

    public ISubscription Subscribe(Func<CanFrameView, bool>? predicate = null, int? bufferCapacity = null)
        => _inner.Subscribe(predicate, bufferCapacity);

    public ISubscription Subscribe(CanIdFilter filter, int? bufferCapacity = null)
        => _inner.Subscribe(filter, bufferCapacity);

    public IReadOnlyList<(ISubscription First, ISubscription Second)> FindOverlappingFilterSubscriptions()
        => _inner.FindOverlappingFilterSubscriptions();

    public Task<TxConfirmation> SendConfirmed(CanFrame frame, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (frame.IsExtendedFrame)
        {
            var fields = J1939Id.Decompose((uint)frame.ID);
            if (J1939Pgn.IsTransportCm(fields.Pgn))
            {
                return Task.FromResult(new TxConfirmation
                {
                    Confirmed = false,
                    IsApproximated = false,
                    Timestamp = DateTime.UtcNow,
                    FailureReason = TxConfirmFailureReason.Rejected,
                });
            }
        }

        return _inner.SendConfirmed(frame, timeout, cancellationToken);
    }

    public void Dispose() { /* leaveOpen wrappers do not own the inner service */ }
}

internal static class J1939TpTestExtensions
{
    public static async Task<T> AsTaskWithTimeout<T>(this Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task) throw new TimeoutException($"Operation timed out after {timeout}.");
        return await task;
    }

    public static async Task WithTimeout(this Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task) throw new TimeoutException($"Operation timed out after {timeout}.");
        await task;
    }
}
