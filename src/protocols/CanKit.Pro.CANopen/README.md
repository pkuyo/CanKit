# CanKit.Pro.CANopen

Experimental **CANopen (CiA 301)** node implementation for CanKit.Pro. Provides an in-process
`ICanOpenNode` that combines a local Object Dictionary, an SDO server + client, an NMT slave
state machine with a heartbeat producer/consumer, a SYNC producer/consumer, structured EMCY
encoding and static TPDO/RPDO mapping — all composed on the CanKit.Pro L2 pipeline
(`ICanBusService` / `IProtocolActor` / `DeadlineScheduler`), exactly like `CanKit.Pro.IsoTp`,
`CanKit.Pro.J1939Tp` and `CanKit.Pro.Uds`.

## Coverage

Every CiA 301 **Must**, **Should** and (formerly deferred) **Could** requirement from SRS
§4.3.2 is implemented:

| SRS id | Feature |
| --- | --- |
| FR-CO-001 | Local Object Dictionary with typed read/write and data-type enforcement |
| FR-CO-002 | SDO expedited transfer (payloads ≤ 4 bytes) |
| FR-CO-003 | SDO segmented transfer (payloads > 4 bytes, toggle-bit protocol) |
| FR-CO-004 | SDO block transfer (CiA 301 §7.2.4.3.15) — client + server, download + upload, blksize negotiation, optional CRC-16/XMODEM |
| FR-CO-005 | Static TPDO/RPDO mapping (byte-aligned, up to 8 bytes) |
| FR-CO-006 | Event/timer TPDO and SYNC-triggered PDO |
| FR-CO-007 | NMT master + slave state machine (Start / Stop / Pre-Op / Reset) |
| FR-CO-008 | Heartbeat producer + consumer timeout event |
| FR-CO-009 | Node-Guarding (CiA 301 §7.2.8.3.3) — RTR-based consumer + producer, life-time timeout event |
| FR-CO-010 | SYNC producer and consumer |
| FR-CO-011 | EMCY encode/decode + structured receive event |
| FR-CO-012 | Uses the L2 `ICanBusService` demux (subscription with COB-ID filter) |

## Open items

* Dynamic PDO mapping via OD 0x1600/0x1A00 rewrite over SDO. Mapping is currently configured
  through the typed `ConfigureTpdo` / `ConfigureRpdo` API, which is enough for the MVP tests
  and for building a canonical mapping into a caller's OD offline.

### SDO block transfer

`SdoDownloadAsync` / `SdoUploadAsync` auto-select the block codec when the payload reaches
`CanOpenNodeOptions.SdoBlockThresholdBytes` (default 128 bytes); callers can force a specific
codec by passing `SdoTransferMode.Block` (or `Expedited` / `Segmented`). The block size
advertised by this node is `CanOpenNodeOptions.SdoBlockSize` (default 127; peers with a
smaller window renegotiate downward). CRC-16/XMODEM is exchanged when both endpoints set the
"cc" / "sc" bit (`SdoBlockCrcSupported`, default `true`).

### Node-guarding

`StartNodeGuardingConsumer(nodeId, guardTime, lifeTimeFactor)` polls `0x700 + nodeId` with an
RTR every `guardTime` and raises `NodeGuardingTimeout` after `guardTime × lifeTimeFactor`
without a valid reply. The producer side answers the RTR with `(toggle << 7) | state` and
flips the toggle bit on every reply; heartbeat and node-guarding are mutually exclusive on
the same producer (`RespondToNodeGuardingRtr` gates the reply, and an active heartbeat
producer takes precedence per CiA 301 §7.2.8.3).

## Quick start

```csharp
using CanKit.Core;
using CanKit.Pro.CANopen;

using var bus = CanBus.Open("virtual://demo/0",
    cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

using var node = CanOpen.OpenNode(bus, nodeId: 0x11);

// FR-CO-001: populate the local OD.
node.ObjectDictionary.AddU32(0x1000, 0x00, 0x00030191, OdAccess.ReadOnly); // device type
node.ObjectDictionary.AddU32(0x2000, 0x00, 0xDEADBEEF);

// FR-CO-002: SDO expedited read from another node on the same bus.
var value = await otherNode.SdoUploadAsync(serverNodeId: 0x11, index: 0x1000, subindex: 0x00);

// FR-CO-008: heartbeat producer + local consumer for a peer.
node.StartHeartbeatProducer(TimeSpan.FromMilliseconds(200));
node.AddHeartbeatConsumer(producerNodeId: 0x12, timeout: TimeSpan.FromMilliseconds(500));
node.HeartbeatTimeout += (s, e) => Console.WriteLine($"missed HB from 0x{e.ProducerNodeId:X2}");

// FR-CO-007: bring the network up as an NMT master.
await node.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0);
```

See `tests/CanKit.Tests/TestCases/CANopen` for end-to-end examples that exercise every FR-CO
requirement over the `CanKit.Adapter.Virtual` loopback bus.

## Layout

```
CanKit.Pro.CANopen/
  CanOpen.cs                 // factory
  CanOpenNode.cs             // implementation of ICanOpenNode
  CanOpenNodeOptions.cs
  CanOpenCobId.cs            // pre-defined connection set constants
  ObjectDictionary.cs, OdEntry.cs
  Nmt/NmtState.cs            // NMT enum + command specifier
  Sdo/                       // codec, abort codes, exception, block-transfer codec + mode enum
  CanOpenNode.SdoBlock.cs    // partial: SDO block transfer (client + server)
  CanOpenNode.NodeGuarding.cs // partial: node-guarding consumer + producer
  Pdo/PdoMapping.cs          // mapping + transmission types
  Emcy/EmcyMessage.cs        // 8-byte encode/decode
```
