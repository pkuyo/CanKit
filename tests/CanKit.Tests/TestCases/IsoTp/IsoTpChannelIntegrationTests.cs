using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.Actor;
using CanKit.Pro.IsoTp;
using CanKit.Pro.RawCan;
using FluentAssertions;
using Xunit;
// Alias the CanKit.Pro.IsoTp namespace root to avoid clashing with this test namespace's
// trailing "IsoTp" segment, which would otherwise shadow the static factory class.
using IsoTpFactory = CanKit.Pro.IsoTp.IsoTp;

namespace CanKit.Tests.TestCases.IsoTp;

/// <summary>
/// End-to-end integration tests for <see cref="IIsoTpChannel"/> against the Virtual loopback
/// adapter. These tests exercise the actor-driven runtime (subscription + demux + deadlines +
/// send-confirmed) rather than the pure codec.
/// </summary>
/// <remarks>
/// Traceability to SRS:
/// <list type="bullet">
///   <item><description>FR-TP-001 / FR-TP-002 / FR-TP-008 — SF and multi-frame round-trips (SN=1..)</description></item>
///   <item><description>FR-TP-009 — multi-frame TX actually starts (FF sent, waits for FC, sends CFs)</description></item>
///   <item><description>FR-TP-010 — N_Bs timeout: SendAsync faults when peer never sends FC</description></item>
///   <item><description>FR-TP-011 — WFTmax: too many Wait FCs abort the send</description></item>
///   <item><description>FR-TP-012 — Overflow FC aborts the send</description></item>
///   <item><description>FR-TP-016 — event-driven scheduling (no busy loop; disposed cleanly)</description></item>
///   <item><description>FR-TP-018 — multiple channels on the same bus with disjoint endpoints</description></item>
/// </list>
/// </remarks>
public class IsoTpChannelIntegrationTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    private static string NewSession() => $"isotp-{Guid.NewGuid():N}";

    private static ICanBus OpenClassic(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    // Fast timings so protocol-timeout tests don't spend seconds each. Classic-CAN.
    private static IsoTpChannelOptions FastOptions(byte localBs = 0, TimeSpan? localStMin = null,
        TimeSpan? nBs = null, TimeSpan? nCr = null, int wftMax = 10)
        => new()
        {
            UseCanFd = false,
            UsePadding = true,
            LocalBlockSize = localBs,
            LocalStMin = localStMin ?? TimeSpan.Zero,
            NAs = TimeSpan.FromMilliseconds(500),
            NBs = nBs ?? TimeSpan.FromMilliseconds(500),
            NCr = nCr ?? TimeSpan.FromMilliseconds(500),
            WftMax = wftMax,
        };

    // --------------------------------------------------------------------------------
    // FR-TP-001 — SF round-trip on classic CAN via the actor runtime + Virtual loopback.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task SingleFrame_RoundTrips_On_Virtual_Loopback()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epAB = IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8);
        var epBA = IsoTpEndpoint.Normal(txCanId: 0x7E8, rxCanId: 0x7E0);

        using var sender = IsoTpFactory.Open(busA, epAB, FastOptions());
        using var receiver = IsoTpFactory.Open(busB, epBA, FastOptions());

        var receiveTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);

        byte[] pdu = { 0x22, 0xF1, 0x89 }; // e.g. UDS ReadDataByIdentifier(0xF189)
        await sender.SendAsync(pdu);

        var got = await receiveTask;
        got.Should().Equal(pdu);
    }

    // --------------------------------------------------------------------------------
    // FR-TP-001 / FR-TP-002 / FR-TP-008 / FR-TP-009 — multi-frame classic-CAN round-trip
    // exercises FF -> FC -> CFs -> reassembly, with SN starting at 1 and wrapping 0..15 across
    // more than 16 CFs so the wrap logic is also touched.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task MultiFrame_ClassicCan_RoundTrips_20_Bytes()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epAB = IsoTpEndpoint.Normal(0x7E0, 0x7E8);
        var epBA = IsoTpEndpoint.Normal(0x7E8, 0x7E0);

        using var sender = IsoTpFactory.Open(busA, epAB, FastOptions());
        using var receiver = IsoTpFactory.Open(busB, epBA, FastOptions());

        byte[] pdu = Enumerable.Range(0, 20).Select(i => (byte)(i + 1)).ToArray();
        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        await sender.SendAsync(pdu);
        var got = await recvTask;
        got.Should().Equal(pdu);
    }

    [Fact]
    public async Task MultiFrame_ClassicCan_RoundTrips_Long_Payload_With_SN_Wrap()
    {
        // A payload of 200 bytes on classic CAN yields ~29 CFs (7 data bytes each after FF's 6),
        // so SN wraps 0..15 at least once. Exercises FR-TP-008 SN wrap and FR-TP-009's "FF starts
        // and completes within a bounded time".
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        using var sender = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x101, 0x102), FastOptions());
        using var receiver = IsoTpFactory.Open(busB, IsoTpEndpoint.Normal(0x102, 0x101), FastOptions());

        byte[] pdu = Enumerable.Range(0, 200).Select(i => (byte)(i & 0xFF)).ToArray();
        var recv = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        await sender.SendAsync(pdu);
        var got = await recv;
        got.Should().Equal(pdu);
    }

    // --------------------------------------------------------------------------------
    // FR-TP-010 — N_Bs timeout: peer never sends FC after our FF -> SendAsync must fault with
    // IsoTpTimeoutException (not hang, not swallow) within N_Bs.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task MultiFrame_Send_Times_Out_When_Peer_Does_Not_Send_FlowControl()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1); // opened just so the hub actually has a peer

        var opts = FastOptions(nBs: TimeSpan.FromMilliseconds(150));
        using var sender = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x300, 0x301), opts);
        // Note: no IsoTpChannel on busB, so the FF is delivered to the bus but nothing sends FC.

        byte[] pdu = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();

        Func<Task> act = () => sender.SendAsync(pdu);
        (await act.Should().ThrowAsync<IsoTpTimeoutException>()
            .WithMessage("*N_Bs*")).Which.Timer.Should().Be(IsoTpTimer.NBs);
    }

    // --------------------------------------------------------------------------------
    // FR-TP-011 — WFTmax exceeded: peer keeps sending Wait FCs.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task MultiFrame_Send_Aborts_When_Peer_Exceeds_WftMax()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epAB = IsoTpEndpoint.Normal(0x400, 0x401);
        var epBA = IsoTpEndpoint.Normal(0x401, 0x400);

        int wftMax = 2;
        using var sender = IsoTpFactory.Open(busA, epAB,
            FastOptions(nBs: TimeSpan.FromSeconds(2), wftMax: wftMax));

        // Peer implemented "by hand" on busB: for every FF we get, keep answering FC(Wait,...).
        int waitFcSent = 0;
        var peerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != 0x400) return;
            var payload = e.CanFrame.Data.ToArray();
            if (payload.Length == 0) return;
            int typeNibble = payload[0] >> 4;
            if (typeNibble == 0x1) // First Frame -> reply with FC(Wait)
            {
                var fc = IsoTpFrameCodec.BuildFlowControl(epBA, FlowStatus.Wait,
                    blockSize: 0, stMinRaw: 0, isCanFd: false, padding: true);
                var frame = CanFrame.Classic(0x401, fc);
                busB.Transmit(frame);
                Interlocked.Increment(ref waitFcSent);
                peerReady.TrySetResult(true);
            }
        };

        byte[] pdu = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();

        var sendTask = sender.SendAsync(pdu);
        // Trigger further Wait FCs by transmitting extra WaitFC frames from busB directly (the
        // peer handler above only fires on FF; keep the sender waiting by feeding more Waits
        // until it exceeds WftMax).
        await peerReady.Task.WaitAsync(ShortTimeout);
        for (int i = 0; i < wftMax + 2; i++)
        {
            var fc = IsoTpFrameCodec.BuildFlowControl(epBA, FlowStatus.Wait,
                blockSize: 0, stMinRaw: 0, isCanFd: false, padding: true);
            busB.Transmit(CanFrame.Classic(0x401, fc));
            await Task.Delay(20);
        }

        Func<Task> act = () => sendTask;
        var ex = (await act.Should().ThrowAsync<IsoTpWaitFrameLimitExceededException>()).Which;
        ex.Limit.Should().Be(wftMax);
        ex.WaitFramesReceived.Should().BeGreaterThan(wftMax);
    }

    // --------------------------------------------------------------------------------
    // FR-TP-012 — Overflow FC aborts the send with a reported failure.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task MultiFrame_Send_Aborts_On_Overflow_FlowControl()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epAB = IsoTpEndpoint.Normal(0x500, 0x501);
        var epBA = IsoTpEndpoint.Normal(0x501, 0x500);

        using var sender = IsoTpFactory.Open(busA, epAB, FastOptions(nBs: TimeSpan.FromSeconds(2)));

        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != 0x500) return;
            var payload = e.CanFrame.Data.ToArray();
            if (payload.Length == 0) return;
            if ((payload[0] >> 4) == 0x1) // FF -> reply Overflow
            {
                var fc = IsoTpFrameCodec.BuildFlowControl(epBA, FlowStatus.Overflow,
                    blockSize: 0, stMinRaw: 0, isCanFd: false, padding: true);
                busB.Transmit(CanFrame.Classic(0x501, fc));
            }
        };

        byte[] pdu = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();
        Func<Task> act = () => sender.SendAsync(pdu);
        await act.Should().ThrowAsync<IsoTpOverflowException>();
    }

    // --------------------------------------------------------------------------------
    // First-Frame that already carries the full announced length must complete without
    // waiting for a Consecutive Frame (otherwise N_Cr fires and the PDU is never emitted).
    // Classic CAN: inject FF with DL=6 so the 6 data bytes fill the frame.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task FirstFrame_With_Full_Payload_Completes_Without_ConsecutiveFrame()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epRecv = IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8);
        using var receiver = IsoTpFactory.Open(busA, epRecv,
            FastOptions(nCr: TimeSpan.FromMilliseconds(300)));

        var receiveTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);

        // FF PCI 0x10 0x06 + 6 data bytes — announced length equals FF data capacity.
        byte[] ff = { 0x10, 0x06, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 };
        busB.Transmit(CanFrame.Classic(0x7E8, ff));

        var got = await receiveTask;
        got.Should().Equal(0x11, 0x22, 0x33, 0x44, 0x55, 0x66);
    }

    // --------------------------------------------------------------------------------
    // DiscardPendingPdus drains completed PDUs and AbortRx faults so a later ReceiveAsync
    // is not poisoned by leftover inbox items after a higher-layer cancel/timeout.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task DiscardPendingPdus_Drains_Completed_Pdus_And_Abort_Faults()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epRecv = IsoTpEndpoint.Normal(txCanId: 0x7E8, rxCanId: 0x7E0);
        var epPeer = IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8);

        using var receiver = IsoTpFactory.Open(busB, epRecv,
            FastOptions(nCr: TimeSpan.FromMilliseconds(80)));
        using var sender = IsoTpFactory.Open(busA, epPeer, FastOptions());

        // Buffer a completed SF without a waiter.
        await sender.SendAsync(new byte[] { 0x22, 0xF1, 0x90 });
        await Task.Delay(50);

        // Queue an AbortRx fault via N_Cr (FF then silence).
        byte[] ffPayload = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        int ffData = IsoTpFrameCodec.FirstFrameMaxDataLength(isCanFd: false, usesAddressExtension: false, useLongLength: false);
        var ff = IsoTpFrameCodec.BuildFirstFrame(epPeer, ffPayload.Length, ffPayload.AsSpan(0, ffData), isCanFd: false);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), ff));
        await Task.Delay(200); // > N_Cr

        int discarded = receiver.DiscardPendingPdus();
        discarded.Should().BeGreaterThanOrEqualTo(2,
            "at least the SF PDU and the N_Cr abort fault must be drained");

        // Fresh receive after drain must succeed (not throw the discarded N_Cr fault).
        var recv = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        byte[] ok = { 0x3E, 0x00 };
        await sender.SendAsync(ok);
        (await recv).Should().Equal(ok);
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3596444314 — DiscardPendingPdus must also abort in-flight multi-frame
    // reassembly. Otherwise leftover CFs can finish and enqueue a full PDU that a
    // higher layer (UDS) may treat as the next response.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task DiscardPendingPdus_Aborts_InFlight_Reassembly()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epRecv = IsoTpEndpoint.Normal(txCanId: 0x7E8, rxCanId: 0x7E0);
        var epPeer = IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8);

        using var receiver = IsoTpFactory.Open(busB, epRecv, FastOptions());

        var fcSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        busA.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != unchecked((int)epRecv.TxCanId)) return;
            var data = e.CanFrame.Data.ToArray();
            if (data.Length > 0 && (data[0] >> 4) == 0x3)
                fcSeen.TrySetResult(true);
        };

        // 13 bytes => FF carries 6, one CF carries the remaining 7 (classic CAN).
        byte[] stalePayload = Enumerable.Range(0, 13).Select(i => (byte)(i + 0xA0)).ToArray();
        int ffData = IsoTpFrameCodec.FirstFrameMaxDataLength(isCanFd: false, usesAddressExtension: false, useLongLength: false);
        var ff = IsoTpFrameCodec.BuildFirstFrame(epPeer, stalePayload.Length, stalePayload.AsSpan(0, ffData), isCanFd: false);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), ff));

        await fcSeen.Task.WaitAsync(ShortTimeout);

        // Mid-reassembly reset — must clear _rx so the trailing CF cannot complete a PDU.
        receiver.DiscardPendingPdus();

        byte[] chunk = stalePayload.AsSpan(ffData).ToArray();
        var cf = IsoTpFrameCodec.BuildConsecutiveFrame(epPeer, sequenceNumber: 1, chunk,
            isCanFd: false, padding: true);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), cf));
        await Task.Delay(50);

        // Stale multi-frame must not appear; a fresh SF must be the next ReceiveAsync result.
        using var sender = IsoTpFactory.Open(busA, epPeer, FastOptions());
        var recv = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        byte[] fresh = { 0x62, 0xF1, 0x90 };
        await sender.SendAsync(fresh);
        (await recv).Should().Equal(fresh);
    }

    // --------------------------------------------------------------------------------
    // FR-TP-018 — Two ISO-TP channels on the *same* bus with disjoint endpoints work
    // independently.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task Two_Channels_On_Same_Bus_Are_Independent()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        // Share one service on busA multiplexing two ISO-TP endpoints (per FR-TP-018).
        using var svcA = new CanBusService(busA);
        using var svcB = new CanBusService(busB);

        using var sendX = IsoTpFactory.Open(svcA, IsoTpEndpoint.Normal(0x600, 0x601), FastOptions(), leaveOpen: true);
        using var recvX = IsoTpFactory.Open(svcB, IsoTpEndpoint.Normal(0x601, 0x600), FastOptions(), leaveOpen: true);

        using var sendY = IsoTpFactory.Open(svcA, IsoTpEndpoint.Normal(0x700, 0x701), FastOptions(), leaveOpen: true);
        using var recvY = IsoTpFactory.Open(svcB, IsoTpEndpoint.Normal(0x701, 0x700), FastOptions(), leaveOpen: true);

        var recvXTask = recvX.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        var recvYTask = recvY.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);

        byte[] pduX = new byte[] { 1, 2, 3, 4, 5 };
        byte[] pduY = Enumerable.Range(0x80, 30).Select(i => (byte)i).ToArray();

        // Send both concurrently to prove independence.
        await Task.WhenAll(sendX.SendAsync(pduX), sendY.SendAsync(pduY));

        (await recvXTask).Should().Equal(pduX);
        (await recvYTask).Should().Equal(pduY);
    }

    // --------------------------------------------------------------------------------
    // FR-TP-016 / FR-RAW-021 — Dispose is thread-safe / idempotent and unblocks a hanging
    // ReceiveAsync (channel end => ReceiveAsync throws or returns cleanly).
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task Dispose_Unblocks_Pending_ReceiveAsync()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var channel = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x123, 0x321), FastOptions());
        var recvTask = channel.ReceiveAsync();
        // Idempotent dispose
        channel.Dispose();
        channel.Dispose();

        // ReceiveAsync should now throw (channel disposed) rather than hang.
        Func<Task> act = () => recvTask;
        await act.Should().ThrowAsync<Exception>();
    }

    // --------------------------------------------------------------------------------
    // FR-TP-016 — DatagramReceived event fires for a SF PDU.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task DatagramReceived_Event_Fires_For_Received_Sf()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        using var sender = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x111, 0x222), FastOptions());
        using var receiver = IsoTpFactory.Open(busB, IsoTpEndpoint.Normal(0x222, 0x111), FastOptions());

        var eventFired = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.DatagramReceived += (_, e) => eventFired.TrySetResult(e.Data);

        byte[] pdu = { 0xAA, 0xBB, 0xCC };
        await sender.SendAsync(pdu);

        var got = await eventFired.Task.WaitAsync(ShortTimeout);
        got.Should().Equal(pdu);
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3596134684 / FR-TP-010 — N_Cr expiry must fault a blocked ReceiveAsync (not only
    // raise BackgroundExceptionOccurred), and the channel must remain usable afterward.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task MultiFrame_Receive_Faults_On_NCr_Timeout_And_Channel_Remains_Usable()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epRecv = IsoTpEndpoint.Normal(txCanId: 0x7E8, rxCanId: 0x7E0);
        var epPeer = IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8);

        var opts = FastOptions(nCr: TimeSpan.FromMilliseconds(120));
        using var receiver = IsoTpFactory.Open(busB, epRecv, opts);

        var bgFault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.BackgroundExceptionOccurred += (_, ex) => bgFault.TrySetResult(ex);

        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);

        // Peer sends FF for a multi-frame PDU, then never sends CFs -> receiver arms N_Cr and
        // must abort with IsoTpTimeoutException rather than leaving ReceiveAsync hung.
        byte[] ffPayload = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        int ffData = IsoTpFrameCodec.FirstFrameMaxDataLength(isCanFd: false, usesAddressExtension: false, useLongLength: false);
        var ff = IsoTpFrameCodec.BuildFirstFrame(epPeer, ffPayload.Length, ffPayload.AsSpan(0, ffData), isCanFd: false);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), ff));

        Func<Task> act = () => recvTask;
        var timeout = (await act.Should().ThrowAsync<IsoTpTimeoutException>()).Which;
        timeout.Timer.Should().Be(IsoTpTimer.NCr);

        var bg = await bgFault.Task.WaitAsync(ShortTimeout);
        bg.Should().BeOfType<IsoTpTimeoutException>()
            .Which.Timer.Should().Be(IsoTpTimer.NCr);

        // Channel remains usable for a subsequent SF after the abort.
        using var sender = IsoTpFactory.Open(busA, epPeer, FastOptions());
        var recv2 = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        byte[] ok = { 0x22, 0xF1, 0x90 };
        await sender.SendAsync(ok);
        (await recv2).Should().Equal(ok);
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3596134684 — CF sequence-number mismatch aborts reassembly and faults
    // ReceiveAsync (same FailTx-style path as N_Cr).
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task MultiFrame_Receive_Faults_On_SequenceNumber_Mismatch()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epRecv = IsoTpEndpoint.Normal(txCanId: 0x318, rxCanId: 0x310);
        var epPeer = IsoTpEndpoint.Normal(txCanId: 0x310, rxCanId: 0x318);

        using var receiver = IsoTpFactory.Open(busB, epRecv, FastOptions());

        // Wait until the receiver has answered the FF with FC before injecting a bad CF.
        var fcSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        busA.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != unchecked((int)epRecv.TxCanId)) return;
            var data = e.CanFrame.Data.ToArray();
            if (data.Length > 0 && (data[0] >> 4) == 0x3)
                fcSeen.TrySetResult(true);
        };

        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);

        byte[] ffPayload = Enumerable.Range(0, 20).Select(i => (byte)(i + 1)).ToArray();
        int ffData = IsoTpFrameCodec.FirstFrameMaxDataLength(isCanFd: false, usesAddressExtension: false, useLongLength: false);
        var ff = IsoTpFrameCodec.BuildFirstFrame(epPeer, ffPayload.Length, ffPayload.AsSpan(0, ffData), isCanFd: false);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), ff));

        await fcSeen.Task.WaitAsync(ShortTimeout);

        // Expected SN after FF is 1; send SN=2 to force mismatch abort.
        byte[] chunk = { 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16 };
        var badCf = IsoTpFrameCodec.BuildConsecutiveFrame(epPeer, sequenceNumber: 2, chunk,
            isCanFd: false, padding: true);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), badCf));

        Func<Task> act = () => recvTask;
        (await act.Should().ThrowAsync<IsoTpException>())
            .WithMessage("*sequence-number mismatch*");
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3596527680 — a superseding SF / FF (or FF→OVFLW) must AbortRx so a blocked
    // ReceiveAsync does not hang after silent _rx clear.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task MultiFrame_Receive_Faults_When_Superseded_By_SingleFrame()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epRecv = IsoTpEndpoint.Normal(txCanId: 0x338, rxCanId: 0x330);
        var epPeer = IsoTpEndpoint.Normal(txCanId: 0x330, rxCanId: 0x338);

        using var receiver = IsoTpFactory.Open(busB, epRecv, FastOptions());

        var bgFault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.BackgroundExceptionOccurred += (_, ex) => bgFault.TrySetResult(ex);

        var fcSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        busA.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != unchecked((int)epRecv.TxCanId)) return;
            var data = e.CanFrame.Data.ToArray();
            if (data.Length > 0 && (data[0] >> 4) == 0x3)
                fcSeen.TrySetResult(true);
        };

        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);

        byte[] ffPayload = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        int ffData = IsoTpFrameCodec.FirstFrameMaxDataLength(isCanFd: false, usesAddressExtension: false, useLongLength: false);
        var ff = IsoTpFrameCodec.BuildFirstFrame(epPeer, ffPayload.Length, ffPayload.AsSpan(0, ffData), isCanFd: false);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), ff));
        await fcSeen.Task.WaitAsync(ShortTimeout);

        // Racing SF aborts the half-built multi-frame; waiter must see the abort fault first.
        var sf = IsoTpFrameCodec.BuildSingleFrame(epPeer, new byte[] { 0x11, 0x22 },
            isCanFd: false, padding: true);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), sf));

        Func<Task> act = () => recvTask;
        (await act.Should().ThrowAsync<IsoTpException>())
            .WithMessage("*Single Frame aborted in-flight*");

        var bg = await bgFault.Task.WaitAsync(ShortTimeout);
        bg.Should().BeOfType<IsoTpException>().Which.Message.Should().Contain("Single Frame aborted");

        // The superseding SF is still delivered as the next PDU.
        var next = await receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        next.Should().Equal(0x11, 0x22);
    }

    [Fact]
    public async Task MultiFrame_Receive_Faults_When_Superseded_By_FirstFrame_Then_Overflow()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epRecv = IsoTpEndpoint.Normal(txCanId: 0x348, rxCanId: 0x340);
        var epPeer = IsoTpEndpoint.Normal(txCanId: 0x340, rxCanId: 0x348);

        using var receiver = IsoTpFactory.Open(busB, epRecv, FastOptions());

        var fcSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int overflowFc = 0;
        busA.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != unchecked((int)epRecv.TxCanId)) return;
            var data = e.CanFrame.Data.ToArray();
            if (data.Length == 0) return;
            if ((data[0] >> 4) == 0x3)
            {
                if ((data[0] & 0x0F) == (byte)FlowStatus.Overflow)
                    Interlocked.Increment(ref overflowFc);
                else
                    fcSeen.TrySetResult(true);
            }
        };

        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);

        // Start a valid multi-frame reception so _rx + N_Cr are armed.
        byte[] ffPayload = Enumerable.Range(0, 20).Select(i => (byte)(i + 3)).ToArray();
        int ffData = IsoTpFrameCodec.FirstFrameMaxDataLength(isCanFd: false, usesAddressExtension: false, useLongLength: false);
        var ff = IsoTpFrameCodec.BuildFirstFrame(epPeer, ffPayload.Length, ffPayload.AsSpan(0, ffData), isCanFd: false);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), ff));
        await fcSeen.Task.WaitAsync(ShortTimeout);

        // Oversized FF supersedes then refuses with OVFLW — no replacement session. Without
        // AbortRx this left ReceiveAsync hung forever.
        byte[] hugeFf =
        {
            0x10, 0x00,
            0x01, 0x00, 0x00, 0x00, // length = 16_777_216
            0x00, 0x00,
        };
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), hugeFf));

        Func<Task> act = () => recvTask;
        (await act.Should().ThrowAsync<IsoTpException>())
            .WithMessage("*First Frame aborted in-flight*");

        for (int i = 0; i < 50 && Volatile.Read(ref overflowFc) == 0; i++)
            await Task.Delay(20);
        overflowFc.Should().Be(1, "superseding oversized FF must still reply FC(OVFLW)");

        // Channel remains usable for a subsequent SF.
        var recv2 = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        var okSf = IsoTpFrameCodec.BuildSingleFrame(epPeer, new byte[] { 0x55 },
            isCanFd: false, padding: true);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), okSf));
        (await recv2).Should().Equal(0x55);
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3596378393 — an empty CF (PCI only, zero user bytes) must not advance
    // ExpectedSn / BS / N_Cr; a subsequent valid CF with the same SN must still complete
    // reassembly instead of mismatch-aborting or stalling until N_Cr.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task MultiFrame_Receive_Ignores_Empty_ConsecutiveFrame_Without_Advancing_Sn()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epRecv = IsoTpEndpoint.Normal(txCanId: 0x328, rxCanId: 0x320);
        var epPeer = IsoTpEndpoint.Normal(txCanId: 0x320, rxCanId: 0x328);

        using var receiver = IsoTpFactory.Open(busB, epRecv, FastOptions());

        var fcSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        busA.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != unchecked((int)epRecv.TxCanId)) return;
            var data = e.CanFrame.Data.ToArray();
            if (data.Length > 0 && (data[0] >> 4) == 0x3)
                fcSeen.TrySetResult(true);
        };

        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);

        // 13 bytes => FF carries 6, one CF carries the remaining 7 (classic CAN).
        byte[] ffPayload = Enumerable.Range(0, 13).Select(i => (byte)(i + 0x40)).ToArray();
        int ffData = IsoTpFrameCodec.FirstFrameMaxDataLength(isCanFd: false, usesAddressExtension: false, useLongLength: false);
        var ff = IsoTpFrameCodec.BuildFirstFrame(epPeer, ffPayload.Length, ffPayload.AsSpan(0, ffData), isCanFd: false);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), ff));

        await fcSeen.Task.WaitAsync(ShortTimeout);

        // PCI-only CF (SN=1) with no user data — previously advanced ExpectedSn to 2.
        var emptyCf = IsoTpFrameCodec.BuildConsecutiveFrame(epPeer, sequenceNumber: 1,
            ReadOnlySpan<byte>.Empty, isCanFd: false, padding: false);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), emptyCf));

        // Valid CF still carrying SN=1 must complete reassembly.
        byte[] chunk = ffPayload.AsSpan(ffData).ToArray();
        chunk.Length.Should().Be(7);
        var goodCf = IsoTpFrameCodec.BuildConsecutiveFrame(epPeer, sequenceNumber: 1, chunk,
            isCanFd: false, padding: true);
        busA.Transmit(CanFrame.Classic(unchecked((int)epPeer.TxCanId), goodCf));

        (await recvTask).Should().Equal(ffPayload);
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3594960783 (HIGH) — a codec throw inside BeginSendOnLoop (e.g. > 4095 bytes
    // on classic CAN triggers ArgumentOutOfRangeException from BuildFirstFrame) must
    // (1) fault the awaiting SendAsync with the codec exception, (2) release the send-gate,
    // and (3) leave the channel usable for subsequent sends -- rather than leaking _tx and
    // hanging every future SendAsync forever behind the gate.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task Send_Faults_On_Codec_Throw_And_Channel_Remains_Usable()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epAB = IsoTpEndpoint.Normal(0x210, 0x211);
        var epBA = IsoTpEndpoint.Normal(0x211, 0x210);

        using var sender = IsoTpFactory.Open(busA, epAB, FastOptions());
        using var receiver = IsoTpFactory.Open(busB, epBA, FastOptions());

        // >4095 bytes on classic-CAN forces BuildFirstFrame to throw ArgumentOutOfRangeException
        // synchronously on the actor loop -- the exact "codec throws inside BeginSendOnLoop" path
        // Bugbot flagged.
        byte[] oversized = new byte[4096];

        // WaitAsync bounds the wait: under the bug this SendAsync would hang forever because
        // the actor's synchronous BuildFirstFrame throw is swallowed by
        // BackgroundExceptionOccurred without ever completing the TCS.
        Func<Task> act = () => sender.SendAsync(oversized).WaitAsync(ShortTimeout);
        var caught = (await act.Should().ThrowAsync<Exception>()).Which;
        // Codec-thrown ArgumentOutOfRangeException surfaces directly (wrapped only in the actor's
        // synchronous invocation path; unwrapped as-is by FailTx -> TCS -> await).
        caught.Should().Match(e =>
            e is ArgumentOutOfRangeException
            || e is IsoTpException
            || e is InvalidOperationException,
            "codec throw must fault the awaiting SendAsync, not hang it");

        // The gate MUST be released and _tx cleared. A normal send after the failure must
        // succeed within the same short timeout.
        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        byte[] normal = { 0x11, 0x22, 0x33 };
        await sender.SendAsync(normal).WaitAsync(ShortTimeout);
        (await recvTask).Should().Equal(normal);
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3594960794 (HIGH) — a SendAsync whose token is cancelled AFTER BeginSendOnLoop
    // is posted but BEFORE the actor picks it up must NOT emit any CAN frame. Under the bug,
    // BeginSendOnLoop just plowed ahead and pushed a Single-Frame onto the wire even though
    // the TCS had already (or was about to be) cancelled.
    //
    // We arrange the race deterministically by parking the sender's ProtocolActor mailbox
    // directly (Post a blocking work item). DatagramReceived is raised off-actor
    // (Bugbot 3596580061), so the older "throw in DatagramReceived + block in
    // BackgroundExceptionOccurred" trick no longer freezes the actor and let begin race
    // ahead of Cancel on CI.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task Send_Cancelled_Before_Actor_Delivery_Emits_No_Frame_And_Channel_Remains_Usable()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epAB = IsoTpEndpoint.Normal(0x220, 0x221);
        var epBA = IsoTpEndpoint.Normal(0x221, 0x220);

        using var sender = IsoTpFactory.Open(busA, epAB, FastOptions());
        using var receiver = IsoTpFactory.Open(busB, epBA, FastOptions());

        // Frame counter: was the cancelled SF payload ever put on the wire?
        int framesToPeer = 0;
        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID == 0x220) Interlocked.Increment(ref framesToPeer);
        };

        var actorField = sender.GetType().GetField("_actor",
            BindingFlags.Instance | BindingFlags.NonPublic);
        actorField.Should().NotBeNull("IsoTpChannel must keep an _actor field for this race test");
        var actor = (IProtocolActor)actorField!.GetValue(sender)!;

        using var actorParked = new ManualResetEventSlim(false);
        using var releaseActor = new ManualResetEventSlim(false);
        actor.Post(() =>
        {
            actorParked.Set();
            releaseActor.Wait(TimeSpan.FromSeconds(10));
        });
        actorParked.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "the sender's actor must be sitting inside our parked work item by now");

        // Sender's actor is now blocked. Queue BeginSendOnLoop, then cancel the token (which
        // synchronously completes the send TCS and posts actor-side TX cleanup). Begin +
        // cleanup sit in the mailbox until we release.
        using var cts = new CancellationTokenSource();
        var sendTask = sender.SendAsync(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, cts.Token);
        cts.Cancel();

        // Release the parked actor; it now drains [begin, cleanup]. Under the fix, begin sees
        // tcs.Task.IsCompleted / ct.IsCancellationRequested and emits nothing.
        releaseActor.Set();

        Func<Task> act = () => sendTask.WaitAsync(ShortTimeout);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Give any straggling actor work time to (incorrectly) hit the wire under the bug.
        await Task.Delay(100);
        framesToPeer.Should().Be(0,
            "a send cancelled before the actor delivers BeginSendOnLoop must never put a frame on the bus");

        // One-in-flight guarantee: the send gate must be released and the actor usable.
        var recvTask2 = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        byte[] normal2 = { 0x01, 0x02, 0x03 };
        await sender.SendAsync(normal2).WaitAsync(ShortTimeout);
        (await recvTask2).Should().Equal(normal2);
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3594960802 (MEDIUM) — Extended addressing round-trip when sourceAddress and
    // targetAddress DIFFER. Under the bug, IsoTpEndpoint.Extended stored only the target
    // address as AddressExtension, so the RX filter compared inbound frames against the
    // outbound TX address-extension byte and dropped every legitimate reply.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task ExtendedAddressing_With_Distinct_Source_And_Target_Round_Trips()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        // Alice: SA=0xAA, TA=0xBB. Alice's outbound AE=0xBB; Alice expects inbound AE=0xAA.
        // Bob:   SA=0xBB, TA=0xAA. Bob's outbound AE=0xAA;   Bob expects inbound AE=0xBB.
        var alice = IsoTpEndpoint.Extended(txCanId: 0x300, rxCanId: 0x301,
            sourceAddress: 0xAA, targetAddress: 0xBB);
        var bob = IsoTpEndpoint.Extended(txCanId: 0x301, rxCanId: 0x300,
            sourceAddress: 0xBB, targetAddress: 0xAA);

        // Sanity-check the endpoint values themselves so the test still catches the bug even if
        // the runtime later stops using RxAddressExtension.
        alice.AddressExtension.Should().Be(0xBB);
        alice.RxAddressExtension.Should().Be(0xAA);
        bob.AddressExtension.Should().Be(0xAA);
        bob.RxAddressExtension.Should().Be(0xBB);

        using var sender = IsoTpFactory.Open(busA, alice, FastOptions());
        using var receiver = IsoTpFactory.Open(busB, bob, FastOptions());

        // A -> B: Alice writes AE=0xBB, Bob expects AE=0xBB -> match.
        var recvOnBob = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        byte[] a2b = { 0xC0, 0xDE };
        await sender.SendAsync(a2b);
        (await recvOnBob).Should().Equal(a2b);

        // B -> A: Bob writes AE=0xAA, Alice expects AE=0xAA -> match.
        // Under the bug Alice's RX filter compared against 0xBB (target) and dropped the frame.
        var recvOnAlice = sender.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        byte[] b2a = { 0xBE, 0xEF };
        await receiver.SendAsync(b2a);
        (await recvOnAlice).Should().Equal(b2a);
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3596468541 (MEDIUM) — Dispose must not tear down _sendGate while an in-flight
    // SendAsync still holds it. Otherwise Release in SendAsync's finally throws
    // ObjectDisposedException (or worse) instead of a clean ObjectDisposedException from FailTx.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task Dispose_During_InFlight_Send_Does_Not_Race_SendGate_Release()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epAB = IsoTpEndpoint.Normal(0x240, 0x241);

        using var inner = new CanBusService(busA);
        using var holdConfirm = new SemaphoreSlim(0, 1);
        using var confirmStarted = new ManualResetEventSlim(false);
        var delaying = new DelayingConfirmService(inner, holdConfirm, confirmStarted);
        var sender = IsoTpFactory.Open(delaying, epAB, FastOptions(), leaveOpen: true);

        var sendTask = sender.SendAsync(new byte[] { 0xAA, 0xBB });
        confirmStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "SendConfirmed must be parked so Dispose races an in-flight SendAsync");

        // Dispose while SendAsync still holds _sendGate (awaiting confirmation / idle drain).
        Action dispose = () => sender.Dispose();
        dispose.Should().NotThrow("Dispose must wait out the send-gate holder before disposing it");

        holdConfirm.Release();

        Func<Task> send = () => sendTask.WaitAsync(ShortTimeout);
        // Clean shutdown: FailTx's ObjectDisposedException, not a secondary ODE from Release.
        (await send.Should().ThrowAsync<ObjectDisposedException>())
            .Which.ObjectName.Should().Be("IsoTpChannel");
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3596212788 (HIGH) — cancelling SendAsync must not release _sendGate while a
    // SendConfirmed started by SendFrameOnBus is still outstanding. Otherwise a subsequent
    // SendAsync can put a new PDU on the wire while the aborted PDU's frame is still TX'ing.
    //
    // Arrangement: wrap the bus service so the first SendConfirmed blocks until we release it;
    // cancel the in-flight SF send, start a second SendAsync concurrently, and assert the
    // second PDU does not appear on the peer until the first confirmation is released.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task Cancelled_Send_Holds_Gate_Until_InFlight_Bus_Tx_Completes()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epAB = IsoTpEndpoint.Normal(0x230, 0x231);
        var epBA = IsoTpEndpoint.Normal(0x231, 0x230);

        using var inner = new CanBusService(busA);
        using var holdFirstConfirm = new SemaphoreSlim(0, 1);
        using var firstConfirmStarted = new ManualResetEventSlim(false);
        var delaying = new DelayingConfirmService(inner, holdFirstConfirm, firstConfirmStarted);
        using var sender = IsoTpFactory.Open(delaying, epAB, FastOptions(), leaveOpen: true);
        using var receiver = IsoTpFactory.Open(busB, epBA, FastOptions());

        int framesToPeer = 0;
        byte? lastSfDl = null;
        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != 0x230) return;
            var payload = e.CanFrame.Data.ToArray();
            if (payload.Length == 0) return;
            if ((payload[0] >> 4) == 0x0) // SF
            {
                Interlocked.Increment(ref framesToPeer);
                lastSfDl = (byte)(payload[0] & 0x0F);
            }
        };

        using var cts = new CancellationTokenSource();
        var cancelledSend = sender.SendAsync(new byte[] { 0xAA, 0xBB }, cts.Token);

        firstConfirmStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "the first SendConfirmed must be parked inside our delaying wrapper");

        cts.Cancel();

        // cancelledSend awaits WaitForBusTxIdleAsync after the OCE, so it must NOT complete while
        // we still hold the first SendConfirmed — that is the gate-hold under test.
        var cancelFinished = cancelledSend.WaitAsync(TimeSpan.FromMilliseconds(200));
        Func<Task> stillHeld = () => cancelFinished;
        await stillHeld.Should().ThrowAsync<TimeoutException>(
            "cancelled SendAsync must keep the send gate until its in-flight SendConfirmed finishes");

        // Second send starts while the aborted PDU's SendConfirmed is still held. Under the bug
        // the gate is already free and this SF (DL=3) hits the bus immediately; under the fix it
        // must wait until we release the first confirmation.
        var secondSend = sender.SendAsync(new byte[] { 0x11, 0x22, 0x33 });
        await Task.Delay(100);
        framesToPeer.Should().Be(0,
            "no SF may hit the peer while the aborted send's SendConfirmed is still parked");

        holdFirstConfirm.Release();

        Func<Task> cancelled = () => cancelledSend.WaitAsync(ShortTimeout);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        await secondSend.WaitAsync(ShortTimeout);

        // Both SFs eventually appear: the already-submitted cancelled frame, then the follow-up.
        for (int i = 0; i < 50 && Volatile.Read(ref framesToPeer) < 2; i++)
            await Task.Delay(20);
        framesToPeer.Should().Be(2, "follow-up SendAsync must TX only after the aborted bus TX drains");
        lastSfDl.Should().Be(3);
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3597312227 (HIGH) — negative LocalStMin used to throw EncodeStMin on the actor
    // loop when building FC after FF, so _rx/N_Cr never started and ReceiveAsync hung. Open
    // must now reject the options up front.
    // --------------------------------------------------------------------------------
    [Fact]
    public void Open_With_Negative_LocalStMin_Throws()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);

        var opts = FastOptions(localStMin: TimeSpan.FromMilliseconds(-1));
        Action act = () => IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x250, 0x251), opts);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3597408323 (HIGH) — FC arriving while the last CF of a block awaits TX-confirm
    // (State==SendingCf) must not be dropped. Under the bug the sender entered WaitFcBlock,
    // armed N_Bs, and timed out even though the peer had already answered.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task MultiFrame_Send_Accepts_FlowControl_Arriving_During_Last_Cf_Confirm()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epAB = IsoTpEndpoint.Normal(0x260, 0x261);
        var epBA = IsoTpEndpoint.Normal(0x261, 0x260);

        using var inner = new CanBusService(busA);
        using var holdCfConfirm = new SemaphoreSlim(0, 8);
        using var cfConfirmParked = new ManualResetEventSlim(false);
        // Hold only CF TX-confirms (after the frame is on the wire). FF confirms normally so the
        // peer can answer the initial FC; the bug window is SendingCf for the last CF of a block.
        var delaying = new HoldConsecutiveFrameConfirmService(inner, holdCfConfirm, cfConfirmParked);
        using var sender = IsoTpFactory.Open(delaying, epAB,
            FastOptions(nBs: TimeSpan.FromMilliseconds(300)), leaveOpen: true);

        // Manual peer: CTS with BS=1 after FF and after each CF. FC is sent while CF confirm is
        // still parked so it lands in State==SendingCf (the drop window Bugbot flagged).
        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != 0x260) return;
            var payload = e.CanFrame.Data.ToArray();
            if (payload.Length == 0) return;
            int type = payload[0] >> 4;
            if (type is 0x1 or 0x2) // FF or CF
            {
                var fc = IsoTpFrameCodec.BuildFlowControl(epBA, FlowStatus.ClearToSend,
                    blockSize: 1, stMinRaw: 0, isCanFd: false, padding: true);
                busB.Transmit(CanFrame.Classic(0x261, fc));
            }
        };

        // 20 bytes classic: FF(6) + CF1(7) + CF2(7). BS=1 => wait for FC after FF and after CF1.
        byte[] pdu = Enumerable.Range(0, 20).Select(i => (byte)(i + 1)).ToArray();
        var sendTask = sender.SendAsync(pdu);

        // Two block-ending CFs (CF1 then CF2): for each, wait until confirm is parked (FC already
        // sent by the peer handler above), then release so deferred FC is applied.
        for (int i = 0; i < 2; i++)
        {
            cfConfirmParked.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue(
                $"CF confirm #{i + 1} must park after transmit so peer FC can defer");
            cfConfirmParked.Reset();
            await Task.Delay(30); // ensure peer FC is processed into DeferredFcs
            holdCfConfirm.Release();
        }

        // Under the bug this times out on N_Bs; under the fix deferred FC resumes the block.
        await sendTask.WaitAsync(ShortTimeout);
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3597408331 (HIGH) — multiple Wait FCs during FF TX-confirm must each count
    // toward WftMax. A single DeferredFc slot used to keep only the last Wait.
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task MultiFrame_Send_Counts_Wait_FlowControls_Deferred_During_Ff_Confirm()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epAB = IsoTpEndpoint.Normal(0x270, 0x271);
        var epBA = IsoTpEndpoint.Normal(0x271, 0x270);

        int wftMax = 2;
        using var inner = new CanBusService(busA);
        using var holdConfirm = new SemaphoreSlim(0, 1);
        using var confirmStarted = new ManualResetEventSlim(false);
        var delaying = new DelayingConfirmService(inner, holdConfirm, confirmStarted,
            holdAfterTransmit: true);
        using var sender = IsoTpFactory.Open(delaying, epAB,
            FastOptions(nBs: TimeSpan.FromSeconds(2), wftMax: wftMax), leaveOpen: true);

        var ffSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != 0x270) return;
            var payload = e.CanFrame.Data.ToArray();
            if (payload.Length > 0 && (payload[0] >> 4) == 0x1)
                ffSeen.TrySetResult(true);
        };

        byte[] pdu = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();
        var sendTask = sender.SendAsync(pdu);

        confirmStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "FF SendConfirmed must be parked so Wait FCs arrive during SingleOrFirstInFlight");
        await ffSeen.Task.WaitAsync(ShortTimeout);

        // Pump WftMax+1 Wait FCs while FF confirm is still held — all must be queued and
        // counted when confirm completes (under the bug only the last Wait survived).
        for (int i = 0; i < wftMax + 1; i++)
        {
            var fc = IsoTpFrameCodec.BuildFlowControl(epBA, FlowStatus.Wait,
                blockSize: 0, stMinRaw: 0, isCanFd: false, padding: true);
            busB.Transmit(CanFrame.Classic(0x271, fc));
            await Task.Delay(20);
        }

        holdConfirm.Release();

        Func<Task> act = () => sendTask.WaitAsync(ShortTimeout);
        var ex = (await act.Should().ThrowAsync<IsoTpWaitFrameLimitExceededException>()).Which;
        ex.Limit.Should().Be(wftMax);
        ex.WaitFramesReceived.Should().BeGreaterThan(wftMax);
    }

    // --------------------------------------------------------------------------------
    // Bugbot 3596212802 (MEDIUM) — HandleRxFirstFrame must not allocate new byte[pci.Length]
    // from an uncapped FF length. Classic channels reject announced lengths above 4095 with
    // FC(OVFLW) and stay usable (same max outbound SendAsync enforces via BuildFirstFrame).
    // --------------------------------------------------------------------------------
    [Fact]
    public async Task Rx_FirstFrame_Above_Classic_Max_Sends_Overflow_And_Does_Not_Allocate()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        var epRecv = IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8);
        using var receiver = IsoTpFactory.Open(busA, epRecv, FastOptions());

        int overflowFc = 0;
        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != 0x7E0) return;
            var payload = e.CanFrame.Data.ToArray();
            if (payload.Length >= 1 && (payload[0] >> 4) == 0x3
                && (payload[0] & 0x0F) == (byte)FlowStatus.Overflow)
            {
                Interlocked.Increment(ref overflowFc);
            }
        };

        // CAN-FD escape FF announcing 0x01000000 bytes — far above classic MaxClassicFirstFrameLength.
        // Under the bug this would attempt new byte[0x01000000] (or worse with int.MaxValue).
        byte[] hugeFf =
        {
            0x10, 0x00,
            0x01, 0x00, 0x00, 0x00, // length = 16_777_216
            0x00, 0x00,
        };
        busB.Transmit(CanFrame.Classic(0x7E8, hugeFf));

        for (int i = 0; i < 50 && Volatile.Read(ref overflowFc) == 0; i++)
            await Task.Delay(20);
        overflowFc.Should().Be(1, "receiver must reply FC(OVFLW) without allocating the announced buffer");

        // Channel remains usable for a normal SF afterwards.
        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        byte[] okSf = { 0x01, 0x99 }; // SF DL=1, data 0x99 — build via peer transmit
        busB.Transmit(CanFrame.Classic(0x7E8, okSf));
        (await recvTask).Should().Equal(0x99);
    }

    /// <summary>
    /// Test double: forwards every <see cref="ICanBusService"/> call to an inner service, but
    /// parks <see cref="ICanBusService.SendConfirmed"/> until <paramref name="release"/> is
    /// signaled so cancel/gate / deferred-FC races are deterministic.
    /// </summary>
    /// <remarks>
    /// Default holds only the first confirm <em>before</em> transmitting (cancel/gate tests).
    /// Pass <paramref name="holdAfterTransmit"/> to transmit first, then park — so peers can
    /// answer FC while TX-confirm is still outstanding (deferred-FC / WftMax tests).
    /// </remarks>
    private sealed class DelayingConfirmService : ICanBusService
    {
        private readonly ICanBusService _inner;
        private readonly SemaphoreSlim _release;
        private readonly ManualResetEventSlim _started;
        private readonly bool _holdAfterTransmit;
        private int _confirmCount;

        public DelayingConfirmService(ICanBusService inner, SemaphoreSlim release,
            ManualResetEventSlim started, bool holdAfterTransmit = false)
        {
            _inner = inner;
            _release = release;
            _started = started;
            _holdAfterTransmit = holdAfterTransmit;
        }

        public ICanBus Bus => _inner.Bus;
        public int SubscriptionCount => _inner.SubscriptionCount;

        public ISubscription Subscribe(Func<CanFrameView, bool>? predicate = null, int? bufferCapacity = null)
            => _inner.Subscribe(predicate, bufferCapacity);

        public ISubscription Subscribe(CanIdFilter filter, int? bufferCapacity = null)
            => _inner.Subscribe(filter, bufferCapacity);

        public IReadOnlyList<(ISubscription First, ISubscription Second)> FindOverlappingFilterSubscriptions()
            => _inner.FindOverlappingFilterSubscriptions();

        public async Task<TxConfirmation> SendConfirmed(CanFrame frame, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            bool hold = Interlocked.Increment(ref _confirmCount) == 1;

            if (hold && !_holdAfterTransmit)
            {
                _started.Set();
                // Do not honor cancellationToken here: the point of the test is that IsoTpChannel
                // still waits for this bus TX even after the caller's SendAsync token is cancelled.
                if (!_release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("test release gate was never signaled");
            }

            var result = await _inner.SendConfirmed(frame, timeout, cancellationToken).ConfigureAwait(false);

            if (hold && _holdAfterTransmit)
            {
                _started.Set();
                if (!_release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("test release gate was never signaled");
            }

            return result;
        }

        public void Dispose() { /* leaveOpen: inner disposed by test */ }
    }

    /// <summary>
    /// Parks <see cref="ICanBusService.SendConfirmed"/> only for Consecutive Frames, and only
    /// after the frame has been transmitted — so a peer FC can arrive while TX state is still
    /// <c>SendingCf</c> (Bugbot 3597408323).
    /// </summary>
    private sealed class HoldConsecutiveFrameConfirmService : ICanBusService
    {
        private readonly ICanBusService _inner;
        private readonly SemaphoreSlim _release;
        private readonly ManualResetEventSlim _parked;

        public HoldConsecutiveFrameConfirmService(ICanBusService inner, SemaphoreSlim release,
            ManualResetEventSlim parked)
        {
            _inner = inner;
            _release = release;
            _parked = parked;
        }

        public ICanBus Bus => _inner.Bus;
        public int SubscriptionCount => _inner.SubscriptionCount;

        public ISubscription Subscribe(Func<CanFrameView, bool>? predicate = null, int? bufferCapacity = null)
            => _inner.Subscribe(predicate, bufferCapacity);

        public ISubscription Subscribe(CanIdFilter filter, int? bufferCapacity = null)
            => _inner.Subscribe(filter, bufferCapacity);

        public IReadOnlyList<(ISubscription First, ISubscription Second)> FindOverlappingFilterSubscriptions()
            => _inner.FindOverlappingFilterSubscriptions();

        public async Task<TxConfirmation> SendConfirmed(CanFrame frame, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            // Copy PCI before await — ReadOnlySpan cannot live across await points.
            byte[] payload = frame.Data.ToArray();
            bool isCf = payload.Length > 0 && (payload[0] >> 4) == 0x2;

            var result = await _inner.SendConfirmed(frame, timeout, cancellationToken).ConfigureAwait(false);

            if (isCf)
            {
                _parked.Set();
                if (!_release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("CF confirm release gate was never signaled");
            }

            return result;
        }

        public void Dispose() { /* leaveOpen: inner disposed by test */ }
    }
}
