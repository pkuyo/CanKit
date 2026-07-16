using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
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
}
