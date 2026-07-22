using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.CANopen;
using CanKit.Pro.CANopen.Nmt;
using CanKit.Pro.CANopen.Sdo;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases.CANopen;

/// <summary>
/// Virtual-loopback integration tests for FR-CO-004 (SDO Block Transfer) and FR-CO-009
/// (Node-Guarding), plus a Virtual-bus RTR-roundtrip canary that catches regressions to the
/// <c>CanFrame.IsRemoteFrame</c> plumbing on which node-guarding depends.
/// </summary>
public class CanOpenBlockAndGuardingTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    private static string NewSession() => $"canopen-{Guid.NewGuid():N}";

    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    // -----------------------------------------------------------------------------------------
    // FR-CO-004: SDO block download roundtrip (~1 KiB payload). The threshold
    // (SdoBlockThresholdBytes default = 128) auto-selects block transfer for a 1024-byte
    // payload; we also verify server-side OD commit.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_BlockDownload_RoundTrip_UpdatesServerOd()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        slave.ObjectDictionary.AddDomain(0x2A00, 0x00, new byte[1024]);

        var payload = new byte[1024];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i & 0xFF);

        await master.SdoDownloadAsync(serverNodeId: 0x11, index: 0x2A00, subindex: 0x00, payload)
            .WithTimeoutAsync(ShortTimeout);

        slave.ObjectDictionary.ReadRaw(0x2A00, 0x00).Should().Equal(payload);
    }

    // Same as above but forces block via SdoTransferMode.Block on a payload that would have
    // gone segmented under the default threshold. Locks in that the mode enum wins over the
    // auto-selection heuristic.
    [Fact]
    public async Task Sdo_BlockDownload_ExplicitMode_ForcesBlockOnSmallPayload()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        slave.ObjectDictionary.AddDomain(0x2A01, 0x00, new byte[40]);

        var payload = Enumerable.Range(0, 40).Select(i => (byte)(0xA0 + i)).ToArray();
        await master.SdoDownloadAsync(serverNodeId: 0x11, index: 0x2A01, subindex: 0x00, payload,
            mode: SdoTransferMode.Block).WithTimeoutAsync(ShortTimeout);

        slave.ObjectDictionary.ReadRaw(0x2A01, 0x00).Should().Equal(payload);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-004: SDO block upload roundtrip. The result trims trailing "unused" bytes reported
    // in the end-of-block frame so the client observes exactly the OD entry, not a multiple of
    // seven.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_BlockUpload_RoundTrip_ReturnsServerOdValue()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        // 300-byte OD entry that is not a multiple of 7, so the last-segment "n" trim is
        // exercised (300 mod 7 = 6 unused bytes on the final segment).
        var payload = new byte[300];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)((i * 7) & 0xFF);
        slave.ObjectDictionary.AddDomain(0x2B00, 0x00, payload, OdAccess.ReadOnly);

        var raw = await master.SdoUploadAsync(serverNodeId: 0x11, index: 0x2B00, subindex: 0x00,
            mode: SdoTransferMode.Block).WithTimeoutAsync(ShortTimeout);

        raw.Should().Equal(payload);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-004: peer with a smaller blksize forces the receiver to send multiple sub-block
    // ACKs. We drive this by capping the master's advertised blksize to 4 and streaming a
    // payload that requires several sub-blocks to complete. Success proves the sub-block
    // ACK / restart-seq path is exercised, not just a single big block.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_BlockUpload_SmallerBlkSize_RenegotiatesAcrossMultipleSubBlocks()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        // Client (master) advertises a tiny 4-segment window so the server has to send
        // multiple sub-blocks.
        var masterOpts = new CanOpenNodeOptions().With(sdoBlockSize: 4);
        using var master = CanOpen.OpenNode(busA, nodeId: 0x01, masterOpts);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        // 100 bytes → ceil(100/7) = 15 segments total → 4 sub-blocks (4+4+4+3).
        var payload = new byte[100];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i ^ 0x5A);
        slave.ObjectDictionary.AddDomain(0x2B01, 0x00, payload, OdAccess.ReadOnly);

        var raw = await master.SdoUploadAsync(serverNodeId: 0x11, index: 0x2B01, subindex: 0x00,
            mode: SdoTransferMode.Block).WithTimeoutAsync(ShortTimeout);

        raw.Should().Equal(payload);
    }

    // FR-CO-004: block download roundtrip with CRC disabled on the master's side. Verifies
    // the "no CRC exchanged" path still trims and commits correctly. Also proves the "cc/sc"
    // negotiation bit is honoured — if the master says "no CRC" the server must not require
    // one for the transfer to succeed.
    [Fact]
    public async Task Sdo_BlockDownload_WithoutCrc_StillSucceeds()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        var masterOpts = new CanOpenNodeOptions().With(sdoBlockCrcSupported: false);
        using var master = CanOpen.OpenNode(busA, nodeId: 0x01, masterOpts);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        slave.ObjectDictionary.AddDomain(0x2A02, 0x00, new byte[512]);
        var payload = new byte[512];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)((i * 31) & 0xFF);

        await master.SdoDownloadAsync(0x11, 0x2A02, 0x00, payload, mode: SdoTransferMode.Block)
            .WithTimeoutAsync(ShortTimeout);
        slave.ObjectDictionary.ReadRaw(0x2A02, 0x00).Should().Equal(payload);
    }

    // -----------------------------------------------------------------------------------------
    // Canary: the Virtual adapter must preserve CanFrame.IsRemoteFrame on the loopback path.
    // This is a prerequisite for FR-CO-009 and is asserted here so a regression to
    // VirtualBusHub / CanFrame.Duplicate is caught by the CANopen test suite too.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Virtual_Rtr_Roundtrip_PreservesIsRemoteFrame()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        var seenRtr = new TaskCompletionSource<(bool rtr, int dataLen)>(TaskCreationOptions.RunContinuationsAsynchronously);
        busB.FrameObserved += (_, e) =>
        {
            var f = e.CanFrame;
            if (f.IsExtendedFrame) return;
            if ((uint)f.ID != 0x711u) return;
            if (!f.IsRemoteFrame) return;
            seenRtr.TrySetResult((f.IsRemoteFrame, f.Data.Length));
        };

        busA.Transmit(CanFrame.Classic(0x711, ReadOnlyMemory<byte>.Empty, isRemoteFrame: true));

        var got = await seenRtr.Task.WithTimeoutAsync(ShortTimeout);
        got.rtr.Should().BeTrue("Virtual bus loopback must round-trip the RTR flag intact");
        got.dataLen.Should().Be(0);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-009: node-guarding happy path. The consumer polls the slave with RTR frames on
    // 0x700+id and the slave replies with (toggle<<7) | state. We assert:
    //   * NodeGuardingReceived is raised with the current state,
    //   * the toggle bit alternates between successive replies (both true and false are seen).
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task NodeGuarding_HappyPath_TogglesBetweenReplies()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        // Slave must not have a heartbeat producer active; RespondToNodeGuardingRtr = true
        // by default so the slave answers RTRs out of the box.
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        // Give the initial bootup frames (which share the 0x700+id COB-ID and are indistin-
        // guishable at the wire level from a node-guarding response with toggle=0) enough time
        // to drain before we register the consumer, otherwise a bootup captured after
        // registration would show up as toggle=false and skew the alternation check.
        await Task.Delay(100);

        var toggles = new List<bool>();
        var states = new List<NmtState>();
        var seenBoth = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        master.NodeGuardingReceived += (_, e) =>
        {
            if (e.ProducerNodeId != 0x11) return;
            lock (toggles)
            {
                toggles.Add(e.Toggle);
                states.Add(e.State);
                // Wait until we have seen at least three responses and the toggle has flipped
                // at least once (both true and false appear). That is the smallest evidence
                // that we're seeing genuine node-guarding replies and not a single stray
                // heartbeat frame.
                if (toggles.Count >= 3 && toggles.Contains(true) && toggles.Contains(false))
                    seenBoth.TrySetResult(true);
            }
        };

        master.StartNodeGuardingConsumer(producerNodeId: 0x11,
            guardTime: TimeSpan.FromMilliseconds(50),
            lifeTimeFactor: 3);

        await seenBoth.Task.WithTimeoutAsync(ShortTimeout);

        lock (toggles)
        {
            toggles.Should().Contain(true, "the producer flips its toggle bit on every reply");
            toggles.Should().Contain(false, "the producer starts with toggle=0");
            // Every observed state must be a valid CANopen NMT state; the slave was left in
            // its post-bootup PreOperational state, so that's what the replies carry.
            states.Should().AllSatisfy(s => s.Should().Be(NmtState.PreOperational));
        }

        master.StopNodeGuardingConsumer(producerNodeId: 0x11);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-009: node-guarding timeout. The slave is configured to *ignore* RTRs
    // (RespondToNodeGuardingRtr=false) so the master's consumer never sees a response and
    // its life-time deadline fires. Also lets us pin the guardTime × lifeTimeFactor semantic
    // (200 ms × 2 = 400 ms → first timeout should fire within a few hundred ms).
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task NodeGuarding_Timeout_FiresWhenSlaveStopsResponding()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        var slaveOpts = new CanOpenNodeOptions().With(respondToNodeGuardingRtr: false);
        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11, slaveOpts);

        var timeout = new TaskCompletionSource<NodeGuardingTimeoutEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        master.NodeGuardingTimeout += (_, e) =>
        {
            if (e.ProducerNodeId == 0x11) timeout.TrySetResult(e);
        };

        master.StartNodeGuardingConsumer(producerNodeId: 0x11,
            guardTime: TimeSpan.FromMilliseconds(100),
            lifeTimeFactor: 2);

        var evt = await timeout.Task.WithTimeoutAsync(ShortTimeout);
        evt.GuardTime.Should().Be(TimeSpan.FromMilliseconds(100));
        evt.LifeTimeFactor.Should().Be((byte)2);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-004 (CiA 301 §7.2.4.3.15): on a partial sub-block ACK the download client must
    // rewind to the first unconfirmed segment and retransmit with the ORIGINAL sub-block
    // seqnos — resending with a restarted seqno=1 would make a compliant peer NACK forever.
    // Driven by a raw-frame fake server on the third bus.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_BlockDownload_Client_Retransmits_With_Original_Seqnos_On_Partial_Ack()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var rawBus = Open(session, 2);

        using var client = CanOpen.OpenNode(busA, nodeId: 0x01);
        var payload = Enumerable.Range(0, 20).Select(i => (byte)(0x30 + i)).ToArray();
        var tap = new FrameTap(rawBus, CanOpenCobId.SdoRx(0x02));

        var sendTask = client.SdoDownloadAsync(serverNodeId: 0x02, index: 0x2100, subindex: 0x00,
            payload, mode: SdoTransferMode.Block, new System.Threading.CancellationTokenSource(ShortTimeout).Token);

        // Block download initiate -> fake server answers with blksize 3, no CRC.
        var init = tap.Next(ShortTimeout);
        (init[0] & 0xE0).Should().Be(SdoBlockFrames.CcsBlockDownloadInitBase);
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoTx(0x02)),
            SdoBlockFrames.BuildBlockDownloadInitResponse(0x2100, 0x00, serverCrcSupported: false, blockSize: 3)));

        // First sub-block: seq 1..3.
        var segs = new List<byte[]>();
        for (var i = 0; i < 3; i++) segs.Add(tap.Next(ShortTimeout));
        segs.Select(s => s[0] & 0x7F).Should().Equal(1, 2, 3);

        // Partial ACK: only the first two segments confirmed.
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoTx(0x02)),
            SdoBlockFrames.BuildSubBlockAck(SdoBlockFrames.ScsBlockDownloadSubBlockAck, lastAckedSeq: 2, nextBlockSize: 3)));

        // The client must rewind and resend starting at seqno 3 with the ORIGINAL numbering
        // (and re-mark the payload's last segment).
        var resent = tap.Next(ShortTimeout);
        (resent[0] & 0x7F).Should().Be(3,
            "the rewind must keep the original sub-block seqno numbering (CiA 301 §7.2.4.3.15)");
        (resent[0] & 0x80).Should().Be(0x80, "the payload's final segment must be re-marked as last");

        // Full ACK for the sub-block; client then sends End; fake server confirms it.
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoTx(0x02)),
            SdoBlockFrames.BuildSubBlockAck(SdoBlockFrames.ScsBlockDownloadSubBlockAck, lastAckedSeq: 3, nextBlockSize: 3)));
        var end = tap.Next(ShortTimeout);
        (end[0] & 0xC3).Should().Be(SdoBlockFrames.CcsBlockDownloadEndBase);
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoTx(0x02)),
            SdoBlockFrames.BuildEndResponse(SdoBlockFrames.ScsBlockDownloadEndResponse)));

        await sendTask.WithTimeoutAsync(ShortTimeout);

        // The peer-side byte stream (seq1, seq2 accepted first, then the resent seq3) must
        // reassemble to exactly the original payload.
        var accepted = segs.Take(2).Select(s => s.Skip(1))
            .Concat(new[] { resent.Skip(1) })
            .SelectMany(b => b).Take(payload.Length).ToArray();
        accepted.Should().Equal(payload);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-004 (CiA 301 §7.2.4.3.15): the upload server applies the same rewind-with-original-
    // seqnos rule when the client partially ACKs a sub-block. Driven by a raw-frame fake client.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_BlockUpload_Server_Retransmits_With_Original_Seqnos_On_Partial_Ack()
    {
        var session = NewSession();
        using var busB = Open(session, 1);
        using var rawBus = Open(session, 2);

        using var server = CanOpen.OpenNode(busB, nodeId: 0x02);
        var payload = Enumerable.Range(0, 20).Select(i => (byte)(0x60 + i)).ToArray();
        server.ObjectDictionary.AddDomain(0x2100, 0x00, payload, OdAccess.ReadOnly);
        var tap = new FrameTap(rawBus, CanOpenCobId.SdoTx(0x02));

        // Fake client initiates the block upload (blksize 3, no CRC, pst=0).
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoRx(0x02)),
            SdoBlockFrames.BuildBlockUploadInit(0x2100, 0x00, clientCrcSupported: false, blockSize: 3, pst: 0)));
        var initResp = tap.Next(ShortTimeout);
        (initResp[0] & 0xE0).Should().Be(SdoBlockFrames.ScsBlockUploadInitResponseBase);
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoRx(0x02)),
            SdoBlockFrames.BuildEndResponse(SdoBlockFrames.CcsBlockUploadStart)));

        // First sub-block: seq 1..3.
        var segs = new List<byte[]>();
        for (var i = 0; i < 3; i++) segs.Add(tap.Next(ShortTimeout));
        segs.Select(s => s[0] & 0x7F).Should().Equal(1, 2, 3);

        // Partial ACK from the client: only two segments confirmed.
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoRx(0x02)),
            SdoBlockFrames.BuildSubBlockAck(SdoBlockFrames.CcsBlockUploadSubBlockAck, lastAckedSeq: 2, nextBlockSize: 3)));

        // The server must rewind and resend seqno 3 with the ORIGINAL numbering.
        var resent = tap.Next(ShortTimeout);
        (resent[0] & 0x7F).Should().Be(3,
            "the upload server must keep the original sub-block seqno numbering on rewind");
        (resent[0] & 0x80).Should().Be(0x80);

        // Full ACK; server sends End; fake client confirms.
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoRx(0x02)),
            SdoBlockFrames.BuildSubBlockAck(SdoBlockFrames.CcsBlockUploadSubBlockAck, lastAckedSeq: 3, nextBlockSize: 3)));
        var end = tap.Next(ShortTimeout);
        (end[0] & 0xC3).Should().Be(SdoBlockFrames.ScsBlockUploadEndBase);
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoRx(0x02)),
            SdoBlockFrames.BuildEndResponse(SdoBlockFrames.CcsBlockUploadEndResponse)));

        var accepted = segs.Take(2).Select(s => s.Skip(1))
            .Concat(new[] { resent.Skip(1) })
            .SelectMany(b => b).Take(payload.Length).ToArray();
        accepted.Should().Equal(payload);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-004: retransmissions are bounded — a peer that never confirms progress aborts the
    // transfer after CanOpenNodeOptions.SdoBlockMaxRetransmissions instead of retrying forever.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_BlockDownload_Aborts_After_Max_Retransmissions()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var rawBus = Open(session, 2);

        var options = new CanOpenNodeOptions().With(sdoBlockMaxRetransmissions: 2);
        using var client = CanOpen.OpenNode(busA, nodeId: 0x01, options);
        var payload = Enumerable.Range(0, 20).Select(i => (byte)(0x30 + i)).ToArray();
        var tap = new FrameTap(rawBus, CanOpenCobId.SdoRx(0x02));

        var sendTask = client.SdoDownloadAsync(serverNodeId: 0x02, index: 0x2100, subindex: 0x00,
            payload, mode: SdoTransferMode.Block, new System.Threading.CancellationTokenSource(ShortTimeout).Token);

        var init = tap.Next(ShortTimeout);
        (init[0] & 0xE0).Should().Be(SdoBlockFrames.CcsBlockDownloadInitBase);
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoTx(0x02)),
            SdoBlockFrames.BuildBlockDownloadInitResponse(0x2100, 0x00, serverCrcSupported: false, blockSize: 3)));

        // First sub-block, then two resumed sub-blocks; the fake server always confirms only
        // the first segment, so the client exceeds its retry budget and must abort (the abort
        // frame is the third tap item after the resumed segments).
        _ = tap.Next(ShortTimeout); // seq 1
        _ = tap.Next(ShortTimeout); // seq 2
        _ = tap.Next(ShortTimeout); // seq 3
        for (var retry = 0; retry < 2; retry++)
        {
            rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoTx(0x02)),
                SdoBlockFrames.BuildSubBlockAck(SdoBlockFrames.ScsBlockDownloadSubBlockAck, lastAckedSeq: 1, nextBlockSize: 3)));
            var resumed = tap.Next(ShortTimeout);
            (resumed[0] & 0x7F).Should().Be(2, "each retry resumes at ackseq + 1 with original seqnos");
            _ = tap.Next(ShortTimeout); // seq 3 of the resumed sub-block
        }
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoTx(0x02)),
            SdoBlockFrames.BuildSubBlockAck(SdoBlockFrames.ScsBlockDownloadSubBlockAck, lastAckedSeq: 1, nextBlockSize: 3)));

        var abort = tap.Next(ShortTimeout);
        abort[0].Should().Be(0x80, "after the retry budget is exhausted the client must emit an SDO abort");
        var ex = await Assert.ThrowsAsync<SdoAbortException>(() => sendTask.WithTimeoutAsync(ShortTimeout));
        ex.AbortCode.Should().Be((uint)SdoAbortCode.General);
    }

    // Raw-frame tap: queues every frame on a given COB-ID so the test body can drive a fake
    // peer deterministically from its own thread (no in-handler transmits).
    private sealed class FrameTap : IDisposable
    {
        private readonly ICanBus _bus;
        private readonly uint _cobId;
        private readonly System.Collections.Concurrent.BlockingCollection<byte[]> _frames = new();

        public FrameTap(ICanBus bus, uint cobId)
        {
            _bus = bus;
            _cobId = cobId;
            _bus.FrameObserved += OnFrame;
        }

        private void OnFrame(object? sender, CanReceiveDataView e)
        {
            if ((uint)e.CanFrame.ID == _cobId)
            {
                _frames.Add(e.CanFrame.Data.ToArray());
            }
        }

        public byte[] Next(TimeSpan timeout)
        {
            if (!_frames.TryTake(out var frame, timeout))
            {
                throw new TimeoutException($"No frame on COB-ID 0x{_cobId:X3} within {timeout}.");
            }
            return frame;
        }

        public void Dispose() => _bus.FrameObserved -= OnFrame;
    }
}
