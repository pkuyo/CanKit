using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.CANopen;
using CanKit.Pro.CANopen.Nmt;
using CanKit.Pro.CANopen.Pdo;
using CanKit.Pro.CANopen.Sdo;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases.CANopen;

/// <summary>
/// Virtual-loopback tests for dynamic PDO mapping over SDO (FR-CO-005 / CiA 301 §7.2.4.6),
/// the classic SDO server session idle timeout, the TPDO event timer, and automatic
/// change-of-state TPDO emission (FR-CO-006).
/// </summary>
public class CanOpenDynamicMappingTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    private static string NewSession() => $"canopen-dynmap-{Guid.NewGuid():N}";

    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    private static byte[] MappingEntryBytes(ushort index, byte subindex, byte bitLength)
    {
        uint raw = ((uint)index << 16) | ((uint)subindex << 8) | bitLength;
        return new[]
        {
            (byte)(raw & 0xFF), (byte)((raw >> 8) & 0xFF),
            (byte)((raw >> 16) & 0xFF), (byte)((raw >> 24) & 0xFF),
        };
    }

    private static async Task WriteMappingAsync(ICanOpenNode client, byte serverNodeId, ushort mapIndex,
        params byte[][] entries)
    {
        await client.SdoDownloadAsync(serverNodeId, mapIndex, 0x00, new byte[] { 0x00 })
            .WithTimeoutAsync(ShortTimeout);
        for (var i = 0; i < entries.Length; i++)
        {
            await client.SdoDownloadAsync(serverNodeId, mapIndex, (byte)(i + 1), entries[i])
                .WithTimeoutAsync(ShortTimeout);
        }
        await client.SdoDownloadAsync(serverNodeId, mapIndex, 0x00, new[] { (byte)entries.Length })
            .WithTimeoutAsync(ShortTimeout);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-005 — dynamic TPDO mapping via SDO (0x1A00), the CiA 301 sub0=0 → subs → sub0=N flow.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_DynamicTpdoMapping_ReconfiguresPayloadViaSdo()
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
        consumer.ConfigureRpdo(1, new PdoMapping().Add(0x2100, 0x00, 16).Add(0x2100, 0x01, 16),
            cobId: producerCobId);

        // Reconfigure TPDO1 on the producer purely over SDO: no ConfigureTpdo API call.
        await WriteMappingAsync(consumer, serverNodeId: 0x11, mapIndex: 0x1A00,
            MappingEntryBytes(0x2000, 0x00, 16),
            MappingEntryBytes(0x2000, 0x01, 16));

        // Read-back: sub0 must report the active entry count, sub1 the encoded first entry.
        var sub0 = await consumer.SdoUploadAsync(0x11, 0x1A00, 0x00).WithTimeoutAsync(ShortTimeout);
        sub0.Should().Equal(0x02, 0x00, 0x00, 0x00);
        var sub1 = await consumer.SdoUploadAsync(0x11, 0x1A00, 0x01).WithTimeoutAsync(ShortTimeout);
        sub1.Should().Equal(MappingEntryBytes(0x2000, 0x00, 16));

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.RpdoReceived += (s, e) =>
        {
            if (e.CobId == producerCobId) received.TrySetResult(e.Payload);
        };

        await consumer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x11);
        await producer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x01);
        await Task.Delay(50);
        await producer.TriggerTpdoAsync(1);

        var payload = await received.Task.WithTimeoutAsync(ShortTimeout);
        payload.Should().Equal(0xEF, 0xBE, 0xAD, 0xDE);
        consumer.ObjectDictionary.ReadUnsigned(0x2100, 0x00).Should().Be((uint)0xBEEF);
        consumer.ObjectDictionary.ReadUnsigned(0x2100, 0x01).Should().Be((uint)0xDEAD);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-005 — dynamic RPDO mapping via SDO (0x1600): the RPDO unpack follows the new mapping.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_DynamicRpdoMapping_ReconfiguresUnpackViaSdo()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var producer = CanOpen.OpenNode(busA, nodeId: 0x11);
        using var consumer = CanOpen.OpenNode(busB, nodeId: 0x01);

        producer.ObjectDictionary.AddU16(0x2000, 0x00, 0x1234);
        consumer.ObjectDictionary.AddU16(0x2100, 0x00, 0);
        consumer.ObjectDictionary.AddU16(0x2100, 0x01, 0);

        // The consumer's RPDO1 lives at its default COB-ID; the producer emits there.
        var rpdoCobId = CanOpenCobId.RpdoDefault(nodeId: 0x01, pdoIndex: 1);
        producer.ConfigureTpdo(1, new PdoMapping().Add(0x2000, 0x00, 16), cobId: rpdoCobId);

        // Remap the consumer's RPDO1 over SDO (from the producer as SDO client): two U16 slots.
        await WriteMappingAsync(producer, serverNodeId: 0x01, mapIndex: 0x1600,
            MappingEntryBytes(0x2100, 0x00, 16),
            MappingEntryBytes(0x2100, 0x01, 16));

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.RpdoReceived += (s, e) =>
        {
            if (e.CobId == rpdoCobId) received.TrySetResult(e.Payload);
        };

        await consumer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x11);
        await producer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x01);
        await Task.Delay(50);
        await producer.TriggerTpdoAsync(1);

        await received.Task.WithTimeoutAsync(ShortTimeout);
        consumer.ObjectDictionary.ReadUnsigned(0x2100, 0x00).Should().Be((uint)0x1234);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-005 — abort paths: writing entries without prior deactivation, mapping a missing
    // OD entry, and exceeding the 8-byte assembled payload.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_DynamicMapping_WriteEntryWhileActive_Aborts()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);
        slave.ObjectDictionary.AddU16(0x2000, 0x00, 0);

        var ex = await Assert.ThrowsAsync<SdoAbortException>(() =>
            master.SdoDownloadAsync(0x11, 0x1A00, 0x01, MappingEntryBytes(0x2000, 0x00, 16))
                .WithTimeoutAsync(ShortTimeout));
        ex.AbortCode.Should().Be((uint)SdoAbortCode.UnsupportedAccess,
            "CiA 301 requires sub0 = 0 (deactivate) before touching mapping entries");
    }

    [Fact]
    public async Task Sdo_DynamicMapping_MissingOdTarget_Aborts()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);

        await master.SdoDownloadAsync(0x11, 0x1A00, 0x00, new byte[] { 0x00 })
            .WithTimeoutAsync(ShortTimeout);

        var ex = await Assert.ThrowsAsync<SdoAbortException>(() =>
            master.SdoDownloadAsync(0x11, 0x1A00, 0x01, MappingEntryBytes(0x2FFF, 0x00, 16))
                .WithTimeoutAsync(ShortTimeout));
        ex.AbortCode.Should().Be((uint)SdoAbortCode.ObjectDoesNotExist);
    }

    [Fact]
    public async Task Sdo_DynamicMapping_ExceedingEightBytes_Aborts()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11);
        slave.ObjectDictionary.AddU32(0x2000, 0x00, 0);
        slave.ObjectDictionary.AddU32(0x2000, 0x01, 0);
        slave.ObjectDictionary.AddU16(0x2001, 0x00, 0);

        await master.SdoDownloadAsync(0x11, 0x1A00, 0x00, new byte[] { 0x00 })
            .WithTimeoutAsync(ShortTimeout);
        await master.SdoDownloadAsync(0x11, 0x1A00, 0x01, MappingEntryBytes(0x2000, 0x00, 32))
            .WithTimeoutAsync(ShortTimeout);
        await master.SdoDownloadAsync(0x11, 0x1A00, 0x02, MappingEntryBytes(0x2000, 0x01, 32))
            .WithTimeoutAsync(ShortTimeout);

        // 4 + 4 + 2 = 10 bytes > the 8-byte classic CAN limit.
        var ex = await Assert.ThrowsAsync<SdoAbortException>(() =>
            master.SdoDownloadAsync(0x11, 0x1A00, 0x03, MappingEntryBytes(0x2001, 0x00, 16))
                .WithTimeoutAsync(ShortTimeout));
        ex.AbortCode.Should().Be((uint)SdoAbortCode.PdoMappingLengthExceeded);
    }

    // -----------------------------------------------------------------------------------------
    // SDO server session idle timeout: a client that opens a segmented download and then goes
    // silent must be dropped after CanOpenNodeOptions.SdoServerTimeout, with an abort on the wire.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Sdo_ServerSession_Expires_With_Abort_When_Peer_Goes_Silent()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var rawBus = Open(session, 2);

        var options = new CanOpenNodeOptions().With(sdoServerTimeout: TimeSpan.FromMilliseconds(150));
        using var master = CanOpen.OpenNode(busA, nodeId: 0x01);
        using var slave = CanOpen.OpenNode(busB, nodeId: 0x11, options);
        slave.ObjectDictionary.AddDomain(0x2100, 0x00, new byte[20]);

        // Raw, manually crafted segmented download initiate (cs = 0x21) against the slave's
        // SDO server, then silence — the client never sends a single segment.
        var init = new byte[] { 0x21, 0x00, 0x21, 0x00, 0x14, 0x00, 0x00, 0x00 };
        rawBus.Transmit(CanFrame.Classic(unchecked((int)CanOpenCobId.SdoRx(0x11)), init));

        // The slave must emit an abort (cs = 0x80) carrying SdoProtocolTimedOut once its
        // server-side idle deadline fires. Read past the init-ack (cs = 0x60) first.
        var sdoTxCobId = CanOpenCobId.SdoTx(0x11);
        using var cts = new CancellationTokenSource(ShortTimeout);
        while (true)
        {
            var frame = (await rawBus.ReceiveAsync(1, 2000, cts.Token))[0];
            var data = frame.CanFrame.Data;
            try
            {
                if ((uint)frame.CanFrame.ID != sdoTxCobId || data.Length < 8 || data.Span[0] != 0x80)
                {
                    continue;
                }
                uint code = (uint)(data.Span[4] | (data.Span[5] << 8) | (data.Span[6] << 16) | (data.Span[7] << 24));
                code.Should().Be((uint)SdoAbortCode.SdoProtocolTimedOut,
                    "the idle segmented server session must be torn down with a timeout abort");
                data.Span[1].Should().Be(0x00, "the abort references the stale transfer's index (LE)");
                data.Span[2].Should().Be(0x21);
                return;
            }
            finally
            {
                frame.CanFrame.Dispose();
            }
        }
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-006 — TPDO event timer: periodic emission without SYNC or manual triggers.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Tpdo_EventTimer_Fires_Periodically_Without_Sync()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var producer = CanOpen.OpenNode(busA, nodeId: 0x11);
        using var consumer = CanOpen.OpenNode(busB, nodeId: 0x01);

        producer.ObjectDictionary.AddU16(0x2000, 0x00, 0xBEEF);
        consumer.ObjectDictionary.AddU16(0x2100, 0x00, 0);

        var producerCobId = CanOpenCobId.TpdoDefault(nodeId: 0x11, pdoIndex: 1);
        producer.ConfigureTpdo(1, new PdoMapping().Add(0x2000, 0x00, 16),
            transmission: TpdoTransmission.EventTimer,
            eventTimerInterval: TimeSpan.FromMilliseconds(50));
        consumer.ConfigureRpdo(1, new PdoMapping().Add(0x2100, 0x00, 16), cobId: producerCobId);

        var count = 0;
        var enough = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.RpdoReceived += (s, e) =>
        {
            if (e.CobId == producerCobId && Interlocked.Increment(ref count) >= 3)
                enough.TrySetResult(count);
        };

        await consumer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x11);
        await producer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x01);

        // No SYNC, no TriggerTpdoAsync — the event timer alone must drive the emissions.
        (await enough.Task.WithTimeoutAsync(ShortTimeout)).Should().BeGreaterOrEqualTo(3);
        consumer.ObjectDictionary.ReadUnsigned(0x2100, 0x00).Should().Be((uint)0xBEEF);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-006 — change-of-state: an application-side OD write emits the event-driven TPDO
    // without any manual TriggerTpdoAsync call.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Tpdo_ChangeOfState_Emits_On_ApplicationOdWrite()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var producer = CanOpen.OpenNode(busA, nodeId: 0x11);
        using var consumer = CanOpen.OpenNode(busB, nodeId: 0x01);

        producer.ObjectDictionary.AddU16(0x2000, 0x00, 0);
        consumer.ObjectDictionary.AddU16(0x2100, 0x00, 0);

        var producerCobId = CanOpenCobId.TpdoDefault(nodeId: 0x11, pdoIndex: 1);
        producer.ConfigureTpdo(1, new PdoMapping().Add(0x2000, 0x00, 16),
            transmission: TpdoTransmission.EventDriven);
        consumer.ConfigureRpdo(1, new PdoMapping().Add(0x2100, 0x00, 16), cobId: producerCobId);

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.RpdoReceived += (s, e) =>
        {
            if (e.CobId == producerCobId) received.TrySetResult(e.Payload);
        };

        await consumer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x11);
        await producer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x01);
        await Task.Delay(50);

        // Application-originated OD write — no TriggerTpdoAsync.
        producer.ObjectDictionary.WriteUnsigned(0x2000, 0x00, 0x1234u);

        var payload = await received.Task.WithTimeoutAsync(ShortTimeout);
        payload.Should().Equal(0x34, 0x12);
        consumer.ObjectDictionary.ReadUnsigned(0x2100, 0x00).Should().Be((uint)0x1234);
    }

    // -----------------------------------------------------------------------------------------
    // FR-CO-006 — echo guard: a bus-originated OD write (RPDO unpack) must NOT re-trigger the
    // node's own change-of-state TPDO, otherwise two nodes mapped to each other would storm.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Tpdo_ChangeOfState_DoesNotEcho_On_RpdoUnpack()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var snifferBus = Open(session, 2);

        using var producer = CanOpen.OpenNode(busA, nodeId: 0x11);
        using var consumer = CanOpen.OpenNode(busB, nodeId: 0x01);

        producer.ObjectDictionary.AddU16(0x2000, 0x00, 0xBEEF);
        consumer.ObjectDictionary.AddU16(0x2100, 0x00, 0);

        var producerCobId = CanOpenCobId.TpdoDefault(nodeId: 0x11, pdoIndex: 1);
        var consumerCobId = CanOpenCobId.TpdoDefault(nodeId: 0x01, pdoIndex: 1);

        // Producer TPDO → consumer RPDO (writes 0x2100:00 on the bus/actor path).
        producer.ConfigureTpdo(1, new PdoMapping().Add(0x2000, 0x00, 16));
        consumer.ConfigureRpdo(1, new PdoMapping().Add(0x2100, 0x00, 16), cobId: producerCobId);
        // The consumer also has an event-driven TPDO mapped to the very same entry: if the
        // RPDO unpack wrongly triggered change-of-state, it would echo on consumerCobId.
        consumer.ConfigureTpdo(1, new PdoMapping().Add(0x2100, 0x00, 16));

        var consumerEmissions = 0;
        snifferBus.FrameObserved += (_, view) =>
        {
            if (view.CanFrame.ID == consumerCobId)
            {
                Interlocked.Increment(ref consumerEmissions);
            }
        };

        await consumer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x11);
        await producer.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x01);
        await Task.Delay(50);
        await producer.TriggerTpdoAsync(1);
        await Task.Delay(300); // give any (wrong) echo ample time to appear

        consumer.ObjectDictionary.ReadUnsigned(0x2100, 0x00).Should().Be((uint)0xBEEF,
            "the RPDO unpack itself must still land in the OD");
        consumerEmissions.Should().Be(0,
            "bus-originated OD writes must not re-trigger change-of-state TPDOs (echo guard)");
    }
}
