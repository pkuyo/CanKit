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
    // Bugbot #3603879808 — a Single Frame that arrives on the response range BEFORE the
    // tester's functional request is TX-confirmed must NOT be included in the collected
    // responses. Only frames that arrive after the request went out on the wire count.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Functional_Collect_Discards_Frames_That_Arrived_Before_Send_Confirmed()
    {
        var session = NewSession();
        using var busA = OpenClassic(session, 0); // tester bus
        using var busB = OpenClassic(session, 1); // ECU bus

        const uint FunctionalTxId = 0x7DF;
        const uint EcuResponseId = 0x7E8;
        const uint StaleEcuResponseId = 0x7E9; // also in-range but sent BEFORE the request
        const uint RangeStart = 0x7E8;
        const uint RangeEnd = 0x7EF;

        // The stale reply — same shape as a real SF response, on an in-range CAN-ID. If the
        // collector treats it as a reply, the assertion will fail.
        byte[] stalePdu = { 0xDE, 0xAD, 0xBE, 0xEF };
        var staleEp = IsoTpEndpoint.Normal(StaleEcuResponseId, 0);
        var staleFrame = CanFrame.Classic(
            unchecked((int)StaleEcuResponseId),
            IsoTpFrameCodec.BuildSingleFrame(staleEp, stalePdu, isCanFd: false, padding: true));

        // The real reply — only sent after the ECU actually observes the functional request.
        byte[] realPdu = { 0x62, 0xF1, 0x90, 0x01 };
        var realEp = IsoTpEndpoint.Normal(EcuResponseId, 0);
        var realFrame = CanFrame.Classic(
            unchecked((int)EcuResponseId),
            IsoTpFrameCodec.BuildSingleFrame(realEp, realPdu, isCanFd: false, padding: true));

        // Open the functional client first so the response filter is subscribed. Now blast the
        // stale SF onto the response CAN-ID while nothing has been sent yet — this is the
        // scenario where an earlier request's late reply is sitting in the pipe.
        using var client = IsoTpFactory.OpenFunctional(busA, FunctionalTxId, RangeStart, RangeEnd,
            FastOptions());

        busB.FrameObserved += (_, e) =>
        {
            if (e.CanFrame.ID == unchecked((int)FunctionalTxId))
                busB.Transmit(realFrame);
        };

        // Emit the stale frame BEFORE the send even begins. Give the virtual loopback a
        // moment to deliver it into the subscription's buffer.
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
            "the pre-send stale SF must be discarded by the gate");
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
