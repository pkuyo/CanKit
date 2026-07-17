using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.CANopen;
using CanKit.Pro.CANopen.Emcy;
using CanKit.Pro.CANopen.Nmt;
using CanKit.Pro.CANopen.Pdo;
using CanKit.Pro.CANopen.Sdo;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases.CANopen;

/// <summary>
/// Virtual-loopback integration tests for the CiA 301 MVP implementation in
/// <c>CanKit.Pro.CANopen</c> (SRS FR-CO-001..012). Two nodes attach to the same Virtual bus so
/// they can exchange NMT / SDO / PDO / SYNC / EMCY / heartbeat traffic exactly like on a real
/// bus, without any hardware.
/// </summary>
public class CanOpenNodeIntegrationTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    private static string NewSession() => $"canopen-{Guid.NewGuid():N}";

    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    // -----------------------------------------------------------------------------------------
    // FR-CO-002 — SDO expedited upload/download over two nodes on the same virtual bus.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_Expedited_Upload_ReturnsServerOdValue()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        slave.ObjectDictionary.AddU32(0x1000, 0x00, 0x00030191u, OdAccess.ReadOnly);

        var raw = await master.SdoUploadAsync(serverNodeId: 0x11, index: 0x1000, subindex: 0x00)
            .WithTimeoutAsync(ShortTimeout);

        raw.Should().Equal(0x91, 0x01, 0x03, 0x00); // little-endian U32
    }

    [Fact]
    public async Task Sdo_Expedited_Download_UpdatesServerOd()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        slave.ObjectDictionary.AddU32(0x2000, 0x00, 0u);

        await master.SdoDownloadAsync(serverNodeId: 0x11, index: 0x2000, subindex: 0x00,
            new byte[] { 0x78, 0x56, 0x34, 0x12 }).WithTimeoutAsync(ShortTimeout);

        slave.ObjectDictionary.ReadUnsigned(0x2000, 0x00).Should().Be(0x12345678u);
    }

    // FR-CO-001 — SDO write into a mismatched fixed-width slot must yield the CiA 301 "length"
    // abort, not silently succeed.
    [Fact]
    public async Task Sdo_Expedited_Download_TypeMismatch_Aborts()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        slave.ObjectDictionary.AddU16(0x2001, 0x00, 0);

        var ex = await Assert.ThrowsAsync<SdoAbortException>(() =>
            master.SdoDownloadAsync(serverNodeId: 0x11, index: 0x2001, subindex: 0x00,
                new byte[] { 0x01, 0x02, 0x03, 0x04 }).WithTimeoutAsync(ShortTimeout));

        ex.AbortCode.Should().Be((uint)SdoAbortCode.LengthTooHigh);
    }

    // FR-CO-001 — SDO read of a missing entry must produce ObjectDoesNotExist, not hang.
    [Fact]
    public async Task Sdo_Upload_MissingEntry_Aborts()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        var ex = await Assert.ThrowsAsync<SdoAbortException>(() =>
            master.SdoUploadAsync(serverNodeId: 0x11, index: 0x2222, subindex: 0x00)
                .WithTimeoutAsync(ShortTimeout));

        ex.AbortCode.Should().Be((uint)SdoAbortCode.ObjectDoesNotExist);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-003 — SDO segmented upload + download, both directions.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_Segmented_Upload_RoundTrip()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        byte[] vin = Encoding.ASCII.GetBytes("WBADT43452G296403"); // 17 bytes, > 4 → segmented
        slave.ObjectDictionary.AddDomain(0x1008, 0x00, vin, OdAccess.ReadOnly);

        var result = await master.SdoUploadAsync(serverNodeId: 0x11, index: 0x1008, subindex: 0x00)
            .WithTimeoutAsync(ShortTimeout);

        result.Should().Equal(vin);
    }

    [Fact]
    public async Task Sdo_Segmented_Download_RoundTrip()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        // Space for 20 bytes; segmented DL because payload > 4.
        slave.ObjectDictionary.AddDomain(0x2100, 0x00, new byte[20]);

        byte[] payload = Enumerable.Range(0, 20).Select(i => (byte)(0x40 + i)).ToArray();
        await master.SdoDownloadAsync(serverNodeId: 0x11, index: 0x2100, subindex: 0x00, payload)
            .WithTimeoutAsync(ShortTimeout);

        slave.ObjectDictionary.ReadRaw(0x2100, 0x00).Should().Equal(payload);
    }

    // Regression for the "expedited initiate leaves stale server session" bug: a partially
    // opened segmented download must not leak into a subsequent unrelated segmented transfer
    // after an expedited initiate has been serviced against the same SDO server. We prove
    // this two ways:
    //   1) segmented → expedited → segmented sequential transfers via the high-level client
    //      still complete correctly (happy-path smoke).
    //   2) a stray in-flight segment frame delivered *after* an expedited initiate — with a
    //      previous segmented session still nominally "open" on the server — must NOT be
    //      applied to the abandoned buffer or committed to the OD. Prior to the fix the
    //      expedited path did not clear _sdoServer, so the stale download session would still
    //      accept and commit those segment frames.
    [Fact]
    public async Task Sdo_ExpeditedInitiate_ClearsStaleSegmentedServerSession()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        // Three OD slots on the server: two segmented-sized domains and one expedited-sized
        // U16. All three must stay individually consistent across the sequence.
        slave.ObjectDictionary.AddDomain(0x2100, 0x00, new byte[20]);
        slave.ObjectDictionary.AddU16(0x2001, 0x00, 0);
        slave.ObjectDictionary.AddDomain(0x2200, 0x00, new byte[17]);

        // --- Part (1): sequential segmented → expedited → segmented all round-trip.
        byte[] seg1 = Enumerable.Range(0, 20).Select(i => (byte)(0x40 + i)).ToArray();
        byte[] seg2 = Enumerable.Range(0, 17).Select(i => (byte)(0x80 + i)).ToArray();

        await master.SdoDownloadAsync(0x11, 0x2100, 0x00, seg1).WithTimeoutAsync(ShortTimeout);
        await master.SdoDownloadAsync(0x11, 0x2001, 0x00, new byte[] { 0x34, 0x12 })
            .WithTimeoutAsync(ShortTimeout);
        await master.SdoDownloadAsync(0x11, 0x2200, 0x00, seg2).WithTimeoutAsync(ShortTimeout);

        slave.ObjectDictionary.ReadRaw(0x2100, 0x00).Should().Equal(seg1);
        slave.ObjectDictionary.ReadUnsigned(0x2001, 0x00).Should().Be((uint)0x1234);
        slave.ObjectDictionary.ReadRaw(0x2200, 0x00).Should().Equal(seg2);

        // --- Part (2): craft a stray segmented-DL init + segment frames on the raw wire so
        // the client-side abort/timeout that master.SdoDownloadAsync always emits cannot mask
        // the stale-session leak. If the fix regresses, the two segment frames below get
        // routed to the still-open segmented session (index 0x3000) and commit the payload
        // [0xAA×7, 0xBB] to 0x3000:00. With the fix, the expedited initiate for 0x2001 that
        // ran right before clears _sdoServer, so the segment frames hit an empty session and
        // are rejected with SdoAbortCode.CommandSpecifierInvalid instead.
        slave.ObjectDictionary.AddDomain(0x3000, 0x00, new byte[8]);

        // Segmented download initiate (cs=0x21) for 0x3000:00, declared length 8.
        var initFrame = new byte[8]
        {
            0x21,               // ccs=1 (init DL), segmented (s=1, e=0)
            0x00, 0x30, 0x00,   // index 0x3000, subindex 0x00
            0x08, 0x00, 0x00, 0x00, // little-endian declared total length = 8
        };
        busA.Transmit(CanFrame.Classic(0x600 + 0x11, initFrame, isExtendedFrame: false));

        // Give the actor loop a moment to install the segmented session for 0x3000.
        await Task.Delay(50);

        // Expedited SDO download to 0x2001 (an unrelated U16 slot). With the fix, this
        // supersedes the still-open 0x3000 segmented session and clears _sdoServer.
        await master.SdoDownloadAsync(0x11, 0x2001, 0x00, new byte[] { 0x78, 0x56 })
            .WithTimeoutAsync(ShortTimeout);

        // Stray segment frames for the now-orphaned 0x3000 transfer. Toggle 0 first, then
        // toggle 1 + last-segment bit.
        // cs bit layout for a download-segment frame: base 0x00, toggle bit 0x10, unused-n in
        // bits 1..3 shifted from n, and continue-bit 0x01 for "no more segments".
        // Segment 1: toggle=0, n=0 (7 bytes valid), last=false → cs=0x00, data [0xAA×7].
        var seg1Frame = new byte[8] { 0x00, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA };
        // Segment 2: toggle=1, n=6 (1 byte valid), last=true → cs = 0x10 | (6<<1) | 0x01 = 0x1D.
        var seg2Frame = new byte[8] { 0x1D, 0xBB, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        busA.Transmit(CanFrame.Classic(0x600 + 0x11, seg1Frame, isExtendedFrame: false));
        busA.Transmit(CanFrame.Classic(0x600 + 0x11, seg2Frame, isExtendedFrame: false));

        // Wait long enough for both segment frames to be processed on the actor loop.
        await Task.Delay(100);

        // Verification:
        //   * With the fix: 0x3000:00 stays untouched (all zeros) because the expedited
        //     initiate cleared the segmented session before either segment frame arrived.
        //   * Without the fix (regression): the segment frames commit [0xAA×7, 0xBB] to
        //     0x3000:00, failing this assertion.
        slave.ObjectDictionary.ReadRaw(0x3000, 0x00).Should().Equal(new byte[8]);

        // And the expedited value we wrote must still be visible.
        slave.ObjectDictionary.ReadUnsigned(0x2001, 0x00).Should().Be((uint)0x5678);
    }

    // Regression for Bugbot Medium (comment 3600499105): a new SDO initiate must abort the
    // previously open server session ON THE WIRE, not just clear it locally. Without this the
    // remote client for the superseded transfer waits until its own SDO timeout instead of
    // hearing about the supersede immediately (CiA 301 §7.2.4.3.4).
    [Fact]
    public async Task Sdo_ServerSupersede_EmitsWireAbort_ForPriorTransfer()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var busObserver = Open(session, 2);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        // Server-side OD: a segmented-sized slot (to be "abandoned") plus an unrelated U16
        // slot the master will supersede it with.
        slave.ObjectDictionary.AddDomain(0x2100, 0x00, new byte[20]);
        slave.ObjectDictionary.AddU16(0x2001, 0x00, 0);

        // Observe every SDO frame the slave emits (COB-ID 0x580+0x11 = 0x591) on a third bus
        // so we can distinguish the slave's own transmits from anything the master sends.
        var slaveSdoTx = new List<byte[]>();
        var abortSeen = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        busObserver.FrameObserved += (_, e) =>
        {
            var frame = e.CanFrame;
            if (frame.IsExtendedFrame) return;
            if ((uint)frame.ID != 0x580u + 0x11u) return;
            var data = frame.Data.ToArray();
            lock (slaveSdoTx) slaveSdoTx.Add(data);
            // cs=0x80 -> SDO abort. Fire completion for the FIRST abort we see with the
            // superseded (index, subindex) = (0x2100, 0x00). That is the marker we care
            // about for this regression; ignore other frames.
            if (data.Length >= 8 && data[0] == 0x80
                && data[1] == 0x00 && data[2] == 0x21 && data[3] == 0x00)
            {
                abortSeen.TrySetResult(data);
            }
        };

        // Push a raw segmented-download initiate for 0x2100:00 onto the wire from busA so the
        // slave installs a server-side segmented session against that (index, subindex) — the
        // "previously open transfer" from CiA 301 §7.2.4.3.4.
        var priorInit = new byte[8]
        {
            0x21,                   // ccs=1 (init download, segmented)
            0x00, 0x21, 0x00,       // index 0x2100, subindex 0x00
            0x14, 0x00, 0x00, 0x00, // declared total length = 20
        };
        busA.Transmit(CanFrame.Classic(0x600 + 0x11, priorInit, isExtendedFrame: false));

        // Give the actor loop a moment to install the segmented session for 0x2100:00.
        await Task.Delay(50);

        // Now supersede: master runs an expedited download to an unrelated (index, subindex)
        // on the same server. Per CiA 301 the server must abort the still-open 0x2100 transfer
        // on the wire; the master's own client for 0x2001 must complete normally (its abort
        // handler ignores mismatched-index aborts intended for the superseded transfer).
        await master.SdoDownloadAsync(0x11, 0x2001, 0x00, new byte[] { 0x78, 0x56 })
            .WithTimeoutAsync(ShortTimeout);

        var abortFrame = await abortSeen.Task.WithTimeoutAsync(ShortTimeout);
        // 8-byte SDO abort layout: cs=0x80, LE(index, subindex), LE(uint32 abort code).
        abortFrame[0].Should().Be((byte)0x80);
        abortFrame[1].Should().Be((byte)0x00); // index low
        abortFrame[2].Should().Be((byte)0x21); // index high
        abortFrame[3].Should().Be((byte)0x00); // subindex
        uint code = (uint)(abortFrame[4] | (abortFrame[5] << 8)
            | (abortFrame[6] << 16) | (abortFrame[7] << 24));
        // The supersede-abort must be a genuine SDO abort code (not something the tests could
        // confuse with the fresh 0x2001 ack, which has cs=0x60).
        code.Should().Be((uint)SdoAbortCode.General);

        // Sanity: the fresh 0x2001 transfer really did commit despite the abort riding the
        // wire alongside it.
        slave.ObjectDictionary.ReadUnsigned(0x2001, 0x00).Should().Be((uint)0x5678);
    }

    // Regression for Copilot 3600429177: a real ECU that strips trailing zeros (DLC < 8) from
    // an SDO server response must not leave our client hanging until its SDO timeout. The
    // client pads short SDO frames back to 8 bytes and lets SdoFrames decode them.
    [Fact]
    public async Task Sdo_ClientResponseWithShortDlc_IsAcceptedAndCompletes()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        // No slave node on busB — just a raw wire we control so we can craft a short-DLC
        // SDO server response and observe how the master's client reacts.
        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);

        // Master initiates an expedited upload from a phantom server 0x11 at (0x2500, 0x00).
        // We do NOT open a slave; instead we fake the server response on busB.
        var uploadTask = master.SdoUploadAsync(serverNodeId: 0x11, index: 0x2500, subindex: 0x00);

        // Give the master a moment to actually put its init request on the wire.
        await Task.Delay(30);

        // Fake a 5-byte SDO expedited upload response: cs=0x4F selects size-indicated with
        // n=3 (one valid byte), followed by index (0x2500), subindex (0x00), and one payload
        // byte 0xAA. A strict "DLC must be 8" implementation would drop this and leave the
        // client waiting until its own SDO timeout fires.
        var shortResponse = new byte[] { 0x4F, 0x00, 0x25, 0x00, 0xAA };
        busB.Transmit(CanFrame.Classic(0x580 + 0x11, shortResponse, isExtendedFrame: false));

        var raw = await uploadTask.WithTimeoutAsync(ShortTimeout);
        raw.Should().Equal(0xAA);
    }

    // Regression for Copilot 3600429203: a length-0 download cannot be represented in the
    // expedited SDO encoding (the 2-bit "n" field cannot distinguish empty from a 4-byte
    // payload). We chose "reject empty downloads at the public API" as the consistent policy;
    // this test locks that choice in and gives users a clear exception instead of silent
    // misencoding.
    [Fact]
    public async Task Sdo_EmptyDownload_ThrowsArgumentException()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);
        slave.ObjectDictionary.AddDomain(0x2600, 0x00, new byte[8]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            master.SdoDownloadAsync(0x11, 0x2600, 0x00, ReadOnlyMemory<byte>.Empty));
    }

    // Regression for Copilot 3600429187: an empty OD value must NOT be served via the
    // expedited response, because expedited cannot represent length 0 (it would decode as
    // four zero bytes on the peer). The server routes empty values through the segmented
    // path, and the client faithfully hands back Array.Empty<byte>().
    [Fact]
    public async Task Sdo_UploadOfEmptyOdValue_ReturnsEmptyArray_ViaSegmented()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        // Empty domain (0 bytes). If the server took the expedited branch, the client would
        // observe 4 zero bytes; via the segmented branch it observes an empty payload.
        slave.ObjectDictionary.AddDomain(0x2700, 0x00, Array.Empty<byte>(), OdAccess.ReadOnly);

        var raw = await master.SdoUploadAsync(serverNodeId: 0x11, index: 0x2700, subindex: 0x00)
            .WithTimeoutAsync(ShortTimeout);
        raw.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-007 + FR-CO-008 — NMT master command transitions the slave and the slave's next
    // heartbeat carries the new state.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Nmt_StartRemoteNode_TransitionsSlaveToOperational()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        var observed = new TaskCompletionSource<NmtState>(TaskCreationOptions.RunContinuationsAsynchronously);
        master.HeartbeatReceived += (s, e) =>
        {
            if (e.ProducerNodeId == 0x11 && e.State == NmtState.Operational)
                observed.TrySetResult(e.State);
        };

        await master.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x11);
        var state = await observed.Task.WithTimeoutAsync(ShortTimeout);

        state.Should().Be(NmtState.Operational);
        slave.State.Should().Be(NmtState.Operational);
    }

    [Fact]
    public async Task Nmt_Broadcast_TransitionsAllNodes()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var busC = Open(session, 2);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave1 = CanOpen.OpenNode(busB, nodeId: 0x11);
        using var slave2 = CanOpen.OpenNode(busC, nodeId: 0x12);

        await master.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0); // broadcast
        await Task.Delay(100); // give the slave loops time to apply
        slave1.State.Should().Be(NmtState.Operational);
        slave2.State.Should().Be(NmtState.Operational);
    }

    // FR-CO-007: Stop then EnterPre-Op state-machine coverage.
    [Fact]
    public async Task Nmt_StopAndPreOp_TransitionsWork()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        await master.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x11);
        await Task.Delay(30);
        slave.State.Should().Be(NmtState.Operational);

        await master.SendNmtCommandAsync(NmtCommand.Stop, targetNodeId: 0x11);
        await Task.Delay(30);
        slave.State.Should().Be(NmtState.Stopped);

        await master.SendNmtCommandAsync(NmtCommand.EnterPreOperational, targetNodeId: 0x11);
        await Task.Delay(30);
        slave.State.Should().Be(NmtState.PreOperational);
    }

    // FR-CO-007: reset node causes a bootup frame (0x00 on 0x700+id) to be re-emitted.
    [Fact]
    public async Task Nmt_ResetNode_EmitsBootup()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        // Wait until the initial bootup from `slave` is consumed.
        await Task.Delay(50);

        var bootup = new TaskCompletionSource<NmtState>(TaskCreationOptions.RunContinuationsAsynchronously);
        master.HeartbeatReceived += (s, e) =>
        {
            if (e.ProducerNodeId == 0x11 && e.State == NmtState.Initializing)
                bootup.TrySetResult(e.State);
        };

        await master.SendNmtCommandAsync(NmtCommand.ResetNode, targetNodeId: 0x11);
        (await bootup.Task.WithTimeoutAsync(ShortTimeout)).Should().Be(NmtState.Initializing);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-008 — heartbeat producer + consumer timeout.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Heartbeat_Producer_Consumer_ObservesLiveNode()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        var seen = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
        master.HeartbeatReceived += (s, e) =>
        {
            if (e.ProducerNodeId == 0x11) seen.TrySetResult(e.ProducerNodeId);
        };

        slave.StartHeartbeatProducer(TimeSpan.FromMilliseconds(50));
        (await seen.Task.WithTimeoutAsync(ShortTimeout)).Should().Be((byte)0x11);
    }

    [Fact]
    public async Task Heartbeat_Consumer_FiresTimeoutWhenPeerGoesSilent()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        // Wait past the initial bootup so the consumer arms cleanly.
        await Task.Delay(100);

        var timeout = new TaskCompletionSource<HeartbeatTimeoutEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        master.HeartbeatTimeout += (s, e) =>
        {
            if (e.ProducerNodeId == 0x11) timeout.TrySetResult(e);
        };
        master.AddHeartbeatConsumer(producerNodeId: 0x11, timeout: TimeSpan.FromMilliseconds(150));

        // Slave never emits another heartbeat -> consumer should fire.
        var evt = await timeout.Task.WithTimeoutAsync(ShortTimeout);
        evt.Timeout.Should().Be(TimeSpan.FromMilliseconds(150));
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-010 — SYNC producer/consumer.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sync_Producer_TriggersReceiver()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        int syncCount = 0;
        var enough = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        slave.SyncReceived += (s, e) =>
        {
            if (Interlocked.Increment(ref syncCount) >= 3) enough.TrySetResult(syncCount);
        };

        master.StartSyncProducer(TimeSpan.FromMilliseconds(20));
        (await enough.Task.WithTimeoutAsync(ShortTimeout)).Should().BeGreaterOrEqualTo(3);

        master.StopSyncProducer();
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-011 — EMCY encode + receive event.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Emcy_Send_TriggersReceiveEventOnPeer()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        var received = new TaskCompletionSource<EmcyMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        master.EmcyReceived += (s, e) => received.TrySetResult(e.Message);

        await slave.SendEmcyAsync(errorCode: 0x8110, errorRegister: 0x01,
            manufacturerSpecific: new byte[] { 0xAA, 0xBB, 0xCC });

        var msg = await received.Task.WithTimeoutAsync(ShortTimeout);
        msg.ProducerNodeId.Should().Be((byte)0x11);
        msg.ErrorCode.Should().Be((ushort)0x8110);
        msg.ErrorRegister.Should().Be((byte)0x01);
        msg.ManufacturerSpecific.Should().Equal(0xAA, 0xBB, 0xCC, 0x00, 0x00);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-005 + FR-CO-006 — TPDO event-triggered mapping + SYNC-triggered TPDO.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Tpdo_EventDriven_Emits_MappedOdValues()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var producer = CanOpen.OpenNode(busA, nodeId: 0x11);
        using var consumer = CanOpen.OpenNode(busB, nodeId: 0x01);

        producer.ObjectDictionary.AddU16(0x2000, 0x00, 0xBEEF);
        producer.ObjectDictionary.AddU16(0x2000, 0x01, 0xDEAD);

        consumer.ObjectDictionary.AddU16(0x2100, 0x00, 0);
        consumer.ObjectDictionary.AddU16(0x2100, 0x01, 0);

        var producerCobId = CanOpenCobId.TpdoDefault(nodeId: 0x11, pdoIndex: 1);

        var producerMapping = new PdoMapping()
            .Add(0x2000, 0x00, 16)
            .Add(0x2000, 0x01, 16);
        producer.ConfigureTpdo(pdoIndex: 1, producerMapping);

        var consumerMapping = new PdoMapping()
            .Add(0x2100, 0x00, 16)
            .Add(0x2100, 0x01, 16);
        consumer.ConfigureRpdo(pdoIndex: 1, consumerMapping, cobId: producerCobId);

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.RpdoReceived += (s, e) =>
        {
            if (e.CobId == producerCobId) received.TrySetResult(e.Payload);
        };

        // Bring the producer into Operational so the TPDO fires.
        await consumer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x11);
        await Task.Delay(50);
        await producer.TriggerTpdoAsync(1);

        var payload = await received.Task.WithTimeoutAsync(ShortTimeout);
        payload.Should().Equal(0xEF, 0xBE, 0xAD, 0xDE);

        consumer.ObjectDictionary.ReadUnsigned(0x2100, 0x00).Should().Be((uint)0xBEEF);
        consumer.ObjectDictionary.ReadUnsigned(0x2100, 0x01).Should().Be((uint)0xDEAD);
    }

    [Fact]
    public async Task Tpdo_SyncTriggered_FiresEverySync()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var producer = CanOpen.OpenNode(busA, nodeId: 0x11);
        using var consumer = CanOpen.OpenNode(busB, nodeId: 0x01);

        producer.ObjectDictionary.AddU32(0x2200, 0x00, 0xCAFEBABE);
        consumer.ObjectDictionary.AddU32(0x2300, 0x00, 0);

        var producerCobId = CanOpenCobId.TpdoDefault(nodeId: 0x11, pdoIndex: 1);
        producer.ConfigureTpdo(1, new PdoMapping().Add(0x2200, 0x00, 32),
            transmission: TpdoTransmission.Synchronous);
        consumer.ConfigureRpdo(1, new PdoMapping().Add(0x2300, 0x00, 32), cobId: producerCobId);

        // Bring producer Operational.
        await consumer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x11);
        await Task.Delay(50);

        int rpdoCount = 0;
        var enough = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.RpdoReceived += (s, e) =>
        {
            if (e.CobId == producerCobId && Interlocked.Increment(ref rpdoCount) >= 2)
                enough.TrySetResult(rpdoCount);
        };

        // Trigger three SYNC frames from the consumer (acting as SYNC producer on our virtual
        // bus). The Synchronous TPDO should fire on each.
        await consumer.SendSyncAsync();
        await consumer.SendSyncAsync();
        await consumer.SendSyncAsync();

        (await enough.Task.WithTimeoutAsync(ShortTimeout)).Should().BeGreaterOrEqualTo(2);
        consumer.ObjectDictionary.ReadUnsigned(0x2300, 0x00).Should().Be(0xCAFEBABEu);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-012 — L2 demux: master runs SDO client + NMT + heartbeat producer traffic
    // simultaneously against the same shared bus service, without any of the three subscriptions
    // starving each other. (We share one ICanBusService between three logical clients on the
    // master side and prove they all still see their events.)
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task L2Demux_MultipleCanOpenNodes_ShareOneBusServiceCleanly()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        // Push some OD state and drive a mix of traffic through the master.
        slave.ObjectDictionary.AddU32(0x2000, 0x00, 0xABCDEF01);
        var sdoRead = master.SdoUploadAsync(serverNodeId: 0x11, index: 0x2000, subindex: 0x00);
        var nmt = master.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x11);
        var sync = master.SendSyncAsync();

        await Task.WhenAll(nmt, sync).WithTimeoutAsync(ShortTimeout);
        var raw = await sdoRead.WithTimeoutAsync(ShortTimeout);
        raw.Should().Equal(0x01, 0xEF, 0xCD, 0xAB);
    }
}

internal static class CanOpenTestExtensions
{
    public static async Task<T> WithTimeoutAsync<T>(this Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed != task) throw new TimeoutException($"Operation timed out after {timeout}.");
        return await task.ConfigureAwait(false);
    }

    public static async Task WithTimeoutAsync(this Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed != task) throw new TimeoutException($"Operation timed out after {timeout}.");
        await task.ConfigureAwait(false);
    }
}
