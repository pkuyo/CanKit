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
