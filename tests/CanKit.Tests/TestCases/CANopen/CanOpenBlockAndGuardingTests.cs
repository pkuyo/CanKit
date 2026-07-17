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
}
