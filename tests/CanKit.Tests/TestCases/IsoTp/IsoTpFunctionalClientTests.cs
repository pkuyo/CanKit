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
using IsoTpFactory = CanKit.Pro.IsoTp.IsoTp;

namespace CanKit.Tests.TestCases.IsoTp;

/// <summary>
/// Integration tests for <see cref="IsoTpFunctionalClient"/> (FR-TP-019) against the virtual
/// loopback adapter.
/// </summary>
/// <remarks>
/// Traceability:
/// <list type="bullet">
///   <item><description>FR-TP-019 — functional (1:N) addressing: SF request + SF responses from ≥ 2 peers</description></item>
/// </list>
/// </remarks>
public class IsoTpFunctionalClientTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CollectionWindow = TimeSpan.FromMilliseconds(300);

    private static string NewSession() => $"isotp-fa-{Guid.NewGuid():N}";

    private static ICanBus OpenClassic(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    private static IsoTpFunctionalOptions FastOptions() => new()
    {
        IsExtendedCanId = false,
        UseCanFd = false,
        UsePadding = true,
        NAs = TimeSpan.FromMilliseconds(500),
    };

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FR-TP-019 (MVP) — functional SF request answered by 2 simulated ECUs; tester collects both.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Request_Collects_SingleFrame_Responses_From_Two_Ecus()
    {
        // Arrange: tester on busA, two ECU peers sharing busB.
        var session = NewSession();
        using var busA = OpenClassic(session, 0); // tester bus
        using var busB = OpenClassic(session, 1); // ECU bus

        // Functional addressing topology (standard UDS 11-bit):
        //   Tester functional TX: 0x7DF
        //   ECU-1 response:       0x7E8
        //   ECU-2 response:       0x7E9
        //   Response range:       0x7E8..0x7EF
        const uint FunctionalTxId = 0x7DF;
        const uint Ecu1ResponseId = 0x7E8;
        const uint Ecu2ResponseId = 0x7E9;
        const uint RangeStart = 0x7E8;
        const uint RangeEnd = 0x7EF;

        // ECU-1 and ECU-2 physical request addresses (the IDs on which they receive from tester):
        // For this test the ECUs just observe any frame on 0x7DF (functional) and reply on their
        // physical response IDs.
        byte[] ecu1Response = { 0x62, 0xF1, 0x90, 0x01 };
        byte[] ecu2Response = { 0x62, 0xF1, 0x91, 0x02 };

        // Build the SF payloads the ECUs will transmit (Normal addressing, no AE byte).
        var ecu1Ep = IsoTpEndpoint.Normal(Ecu1ResponseId, 0);
        var ecu2Ep = IsoTpEndpoint.Normal(Ecu2ResponseId, 0);

        var ecu1Frame = CanFrame.Classic(
            unchecked((int)Ecu1ResponseId),
            IsoTpFrameCodec.BuildSingleFrame(ecu1Ep, ecu1Response, isCanFd: false, padding: true));
        var ecu2Frame = CanFrame.Classic(
            unchecked((int)Ecu2ResponseId),
            IsoTpFrameCodec.BuildSingleFrame(ecu2Ep, ecu2Response, isCanFd: false, padding: true));

        // ECU peers: reply when they see the functional request on 0x7DF.
        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != unchecked((int)FunctionalTxId))
                return;
            busB.Transmit(ecu1Frame);
            busB.Transmit(ecu2Frame);
        };

        // Act: open functional client on busA and broadcast the request.
        using var client = IsoTpFactory.OpenFunctional(busA, FunctionalTxId, RangeStart, RangeEnd,
            FastOptions());

        byte[] request = { 0x22, 0xF1, 0x90 }; // UDS ReadDataByIdentifier(0xF190)
        var responses = await client.SendAndCollectAsync(request, CollectionWindow)
            .WaitAsync(ShortTimeout);

        // Assert: both ECU responses collected.
        responses.Should().HaveCount(2, "both ECUs must respond within the collection window");

        var byCanId = responses.ToDictionary(r => r.SourceCanId);
        byCanId.Should().ContainKey(Ecu1ResponseId);
        byCanId.Should().ContainKey(Ecu2ResponseId);
        byCanId[Ecu1ResponseId].Data.Should().Equal(ecu1Response);
        byCanId[Ecu2ResponseId].Data.Should().Equal(ecu2Response);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FR-TP-019 — empty window returns no responses when no ECU replies.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Request_With_No_Ecus_Returns_Empty_List()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1); // joined so the hub forwards frames, but silent

        using var client = IsoTpFactory.OpenFunctional(busA, 0x7DF, 0x7E8, 0x7EF, FastOptions());

        var responses = await client
            .SendAndCollectAsync(new byte[] { 0x22, 0xF1, 0x90 }, TimeSpan.FromMilliseconds(50))
            .WaitAsync(ShortTimeout);

        responses.Should().BeEmpty("no ECU replied within the window");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FR-TP-019 — outbound PDU too large for a Single Frame must fault with
    // InvalidOperationException (ISO 15765-2 §9.4 SF-only restriction).
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Send_Rejects_MultiFrame_Sized_Pdu()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);

        using var client = IsoTpFactory.OpenFunctional(busA, 0x7DF, 0x7E8, 0x7EF, FastOptions());

        // Classic CAN SF max is 7 bytes; 8 bytes must be rejected.
        byte[] oversized = new byte[8];
        Func<Task> act = () => client.SendAndCollectAsync(oversized, TimeSpan.FromMilliseconds(50));
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Single Frame*");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FR-TP-019 — only ECUs within the response range are collected; frames outside the range
    // are ignored.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Collect_Ignores_Responses_Outside_Range()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        const uint FunctionalTxId = 0x7DF;
        const uint InRangeId = 0x7E8;
        const uint OutOfRangeId = 0x600; // outside 0x7E8..0x7EF
        const uint RangeStart = 0x7E8;
        const uint RangeEnd = 0x7EF;

        byte[] inRangeResponse = { 0x50, 0x03 };
        byte[] outOfRangeResponse = { 0xDE, 0xAD };

        var inEp = IsoTpEndpoint.Normal(InRangeId, 0);
        var outEp = IsoTpEndpoint.Normal(OutOfRangeId, 0);

        var inFrame = CanFrame.Classic(unchecked((int)InRangeId),
            IsoTpFrameCodec.BuildSingleFrame(inEp, inRangeResponse, isCanFd: false, padding: true));
        var outFrame = CanFrame.Classic(unchecked((int)OutOfRangeId),
            IsoTpFrameCodec.BuildSingleFrame(outEp, outOfRangeResponse, isCanFd: false, padding: true));

        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != unchecked((int)FunctionalTxId))
                return;
            busB.Transmit(inFrame);
            busB.Transmit(outFrame);
        };

        using var client = IsoTpFactory.OpenFunctional(busA, FunctionalTxId, RangeStart, RangeEnd,
            FastOptions());

        var responses = await client
            .SendAndCollectAsync(new byte[] { 0x10, 0x03 }, CollectionWindow)
            .WaitAsync(ShortTimeout);

        responses.Should().HaveCount(1, "only the in-range ECU response must be collected");
        responses[0].SourceCanId.Should().Be(InRangeId);
        responses[0].Data.Should().Equal(inRangeResponse);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FR-TP-019 — First-Frame responses must be silently dropped (SF-only collect).
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Collect_Drops_FirstFrame_Responses()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        const uint FunctionalTxId = 0x7DF;
        const uint EcuResponseId = 0x7E8;

        // Build an FF payload (classic CAN, announced length 20, 6 data bytes in FF).
        var peerEp = IsoTpEndpoint.Normal(EcuResponseId, 0);
        byte[] longPayload = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        int ffData = IsoTpFrameCodec.FirstFrameMaxDataLength(isCanFd: false,
            usesAddressExtension: false, useLongLength: false);
        var ffPayload = IsoTpFrameCodec.BuildFirstFrame(peerEp, longPayload.Length,
            longPayload.AsSpan(0, ffData), isCanFd: false);
        var ffFrame = CanFrame.Classic(unchecked((int)EcuResponseId), ffPayload);

        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != unchecked((int)FunctionalTxId))
                return;
            busB.Transmit(ffFrame);
        };

        using var client = IsoTpFactory.OpenFunctional(busA, FunctionalTxId, 0x7E8, 0x7EF,
            FastOptions());

        var responses = await client
            .SendAndCollectAsync(new byte[] { 0x22, 0xF1, 0x90 }, CollectionWindow)
            .WaitAsync(ShortTimeout);

        responses.Should().BeEmpty(
            "First-Frame responses cannot be reassembled in functional addressing and must be dropped");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // Bugbot #3603879808 / #3604506438 — an unrelated Single Frame that was already sitting
    // in the RX pipeline before the tester issued its functional request must NOT be treated
    // as a reply to that request. SendAndCollectAsync enforces this via drain-before-send:
    // subscribe, synchronously drop everything already queued on the subscription, then send.
    //
    // The test emits a spurious in-range SF, gives the loopback a moment to route it, and
    // then calls SendAndCollectAsync. If the spurious frame reached the subscription buffer
    // between Subscribe and DrainBuffered it must be dropped; if it arrived before Subscribe
    // it was never a candidate. In either case the caller must see only the real ECU reply.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Collect_Discards_Frames_That_Arrived_Before_Send()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0); // tester bus
        using var busB = OpenClassic(session, 1); // ECU bus

        const uint FunctionalTxId = 0x7DF;
        const uint EcuResponseId = 0x7E8;
        const uint StaleEcuResponseId = 0x7E9; // also in-range but transmitted BEFORE the request
        const uint RangeStart = 0x7E8;
        const uint RangeEnd = 0x7EF;

        byte[] stalePdu = { 0xDE, 0xAD, 0xBE, 0xEF };
        var staleEp = IsoTpEndpoint.Normal(StaleEcuResponseId, 0);
        var staleFrame = CanFrame.Classic(
            unchecked((int)StaleEcuResponseId),
            IsoTpFrameCodec.BuildSingleFrame(staleEp, stalePdu, isCanFd: false, padding: true));

        byte[] realPdu = { 0x62, 0xF1, 0x90, 0x01 };
        var realEp = IsoTpEndpoint.Normal(EcuResponseId, 0);
        var realFrame = CanFrame.Classic(
            unchecked((int)EcuResponseId),
            IsoTpFrameCodec.BuildSingleFrame(realEp, realPdu, isCanFd: false, padding: true));

        using var client = IsoTpFactory.OpenFunctional(busA, FunctionalTxId, RangeStart, RangeEnd,
            FastOptions());

        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID == unchecked((int)FunctionalTxId))
                busB.Transmit(realFrame);
        };

        // Blast the stale SF into the pipe. Give the virtual hub a moment to route it —
        // if it lands after SendAndCollectAsync's internal Subscribe, DrainBuffered must
        // drop it; if it lands before Subscribe, the subscription never sees it. Either
        // way the assertion below must hold.
        busB.Transmit(staleFrame);
        await Task.Delay(50);

        byte[] request = { 0x22, 0xF1, 0x90 };
        var responses = await client.SendAndCollectAsync(request, CollectionWindow)
            .WaitAsync(ShortTimeout);

        responses.Should().HaveCount(1,
            "only the reply that arrived AFTER the functional request went out counts");
        responses[0].SourceCanId.Should().Be(EcuResponseId);
        responses[0].Data.Should().Equal(realPdu);
        responses.Should().NotContain(r => r.SourceCanId == StaleEcuResponseId,
            "the pre-send stale SF must be discarded by drain-before-send");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // Regression — a fast ECU whose reply lands during or immediately after the driver's
    // TX-confirm must not be dropped. Drain-before-send discards only what is buffered at the
    // instant of the drain call, so any frame delivered afterwards (which necessarily includes
    // every legitimate reply, because the request has not yet gone out on the wire when Drain
    // runs) is a candidate for collection. Every iteration must yield exactly one reply.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Collect_Accepts_Fast_Reply_During_Send()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        const uint FunctionalTxId = 0x7DF;
        const uint EcuResponseId = 0x7E8;

        byte[] realPdu = { 0x62, 0xF1, 0x90, 0x01 };
        var realEp = IsoTpEndpoint.Normal(EcuResponseId, 0);
        var realFrame = CanFrame.Classic(
            unchecked((int)EcuResponseId),
            IsoTpFrameCodec.BuildSingleFrame(realEp, realPdu, isCanFd: false, padding: true));

        // ECU replies inline the moment it observes the functional request. On the virtual
        // hub this makes the reply race the driver's TX-confirm return — the reply may
        // already be buffered on our subscription by the time SendSingleFrameAsync returns.
        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID == unchecked((int)FunctionalTxId))
                busB.Transmit(realFrame);
        };

        using var client = IsoTpFactory.OpenFunctional(busA, FunctionalTxId, 0x7E8, 0x7EF,
            FastOptions());

        // Repeat enough times to stress the ordering. A single dropped reply fails the assertion.
        const int iterations = 30;
        for (int i = 0; i < iterations; i++)
        {
            byte[] request = { 0x22, 0xF1, 0x90 };
            var responses = await client.SendAndCollectAsync(request, CollectionWindow)
                .WaitAsync(ShortTimeout);

            responses.Should().HaveCount(1,
                $"iteration {i}: the fast ECU reply must not be dropped by drain-before-send");
            responses[0].SourceCanId.Should().Be(EcuResponseId);
            responses[0].Data.Should().Equal(realPdu);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // Bugbot 3604648050 regression — every ECU reply that reaches the subscription channel
    // inside the collection window must be surfaced to the caller, including frames that are
    // still sitting in the channel's buffer when the window CTS fires. The pre-fix
    // `CollectFromSubscriptionAsync` used `await foreach` alone: once the window token
    // cancelled the enumerator, any Single-Frame reply that had already been written to the
    // channel but not yet yielded was silently dropped when the subscription was disposed.
    //
    // The fix drains the channel synchronously with `TryRead` in the OperationCanceledException
    // catch, so buffered replies survive the transition from await-yield to window-expiry.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Collect_Drains_Buffered_Frames_On_Window_Expiry()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        const uint FunctionalTxId = 0x7DF;
        const uint EcuResponseId = 0x7E8;
        const int ReplyCount = 12;

        // Pre-build ReplyCount distinct SF frames so we can assert every one was collected
        // (each carries a unique last byte so we can spot duplicates or drops).
        var replies = new List<CanFrame>(ReplyCount);
        for (int i = 0; i < ReplyCount; i++)
        {
            byte[] pdu = { 0x62, 0xF1, 0x90, (byte)i };
            var ep = IsoTpEndpoint.Normal(EcuResponseId, 0);
            replies.Add(CanFrame.Classic(
                unchecked((int)EcuResponseId),
                IsoTpFrameCodec.BuildSingleFrame(ep, pdu, isCanFd: false, padding: true)));
        }

        // The virtual hub calls `Transmit` synchronously and delivers each frame to observers
        // before returning, so by the time our request's TX-confirm returns every reply is
        // already sitting in the tester subscription's channel buffer. That is exactly the
        // shape that provoked the pre-fix drop: frames buffered on the subscription channel
        // but not yet yielded by the enumerator when the window CTS fires.
        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID != unchecked((int)FunctionalTxId)) return;
            foreach (var f in replies) busB.Transmit(f);
        };

        using var client = IsoTpFactory.OpenFunctional(busA, FunctionalTxId, 0x7E8, 0x7EF,
            FastOptions());

        byte[] request = { 0x22, 0xF1, 0x90 };
        // Zero-length collection window: TX-confirm returns synchronously with all ReplyCount
        // frames already in the subscription buffer, then `CancelAfter(TimeSpan.Zero)` fires
        // the window CTS immediately — the enumerator's `MoveNextAsync` throws OCE without
        // ever yielding a frame. This makes the drain-on-catch (Bugbot 3604648050) the ONLY
        // path that can surface the buffered replies to the caller: pre-fix, the returned
        // list would be empty even though every reply arrived on time.
        var responses = await client
            .SendAndCollectAsync(request, TimeSpan.Zero)
            .WaitAsync(ShortTimeout);

        responses.Should().HaveCount(ReplyCount,
            "every reply that arrived on the subscription before the window CTS fired must " +
            "be surfaced, even when the window expires before the enumerator yields any " +
            "frame — the OCE catch must drain the buffered channel (Bugbot 3604648050)");

        // Verify identity — no duplication and no data corruption from the drain path.
        var lastBytes = responses.Select(r => r.Data[3]).OrderBy(b => b).ToArray();
        lastBytes.Should().Equal(Enumerable.Range(0, ReplyCount).Select(i => (byte)i));
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FR-TP-019 — Dispose is idempotent and stops the client.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Functional_Client_Dispose_Is_Idempotent()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);

        var client = IsoTpFactory.OpenFunctional(busA, 0x7DF, 0x7E8, 0x7EF, FastOptions());
        client.Dispose();
        Action secondDispose = () => client.Dispose();
        secondDispose.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FR-TP-019 — Send/Collect after Dispose throws ObjectDisposedException.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Client_Throws_After_Dispose()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);

        var client = IsoTpFactory.OpenFunctional(busA, 0x7DF, 0x7E8, 0x7EF, FastOptions());
        client.Dispose();

        Func<Task> act = () => client.SendAndCollectAsync(
            new byte[] { 0x10, 0x03 }, TimeSpan.FromMilliseconds(10));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FR-TP-019 — SendAsync sends only (no collection), and the same service can be shared with
    // physical IIsoTpChannel instances on disjoint IDs (FR-TP-018 compatibility).
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Client_SendAsync_Puts_Frame_On_Bus()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        int framesObserved = 0;
        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID == unchecked((int)0x7DF))
                Interlocked.Increment(ref framesObserved);
        };

        using var client = IsoTpFactory.OpenFunctional(busA, 0x7DF, 0x7E8, 0x7EF, FastOptions());
        await client.SendAsync(new byte[] { 0x3E, 0x00 }).WaitAsync(ShortTimeout);

        // Give the loopback a moment to deliver.
        for (int i = 0; i < 50 && Volatile.Read(ref framesObserved) == 0; i++)
            await Task.Delay(10);

        framesObserved.Should().Be(1, "exactly one functional SF must appear on the bus");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FR-TP-019 — OpenFunctional with a shared ICanBusService (leaveOpen=true) does not dispose
    // the service on its own Dispose, letting other channels keep using it.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Client_LeaveOpen_Does_Not_Dispose_Service()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        using var svc = new CanBusService(busA);

        // Physical channel on the same service.
        using var physicalChannel = IsoTpFactory.Open(svc,
            IsoTpEndpoint.Normal(0x700, 0x701), leaveOpen: true);

        // Functional client on the same service (disjoint response range from the physical channel).
        var functional = IsoTpFactory.OpenFunctional(svc, 0x7DF, 0x7E8, 0x7EF,
            leaveOpen: true);
        functional.Dispose(); // must not dispose svc

        // Physical channel must still work after functional client is disposed.
        using var peer = IsoTpFactory.Open(busB, IsoTpEndpoint.Normal(0x701, 0x700));
        var recvTask = physicalChannel.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        await peer.SendAsync(new byte[] { 0x11, 0x22 }).WaitAsync(ShortTimeout);
        (await recvTask).Should().Equal(0x11, 0x22);
    }
}
