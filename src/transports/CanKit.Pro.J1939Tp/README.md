# CanKit.Pro.J1939Tp

SAE J1939-21 Transport Protocol (TP) for CanKit.Pro. Implements both flavors of the J1939-21 §5.10 transport service:

- **TP.BAM** (Broadcast Announce Message) — one sender pushes an up-to-1785-byte PDU to every node on the bus, no acknowledgement (FR-TP-030).
- **TP.CM** (Connection Mode: RTS / CTS / EndOfMsgAck / Connection Abort) — point-to-point, with block-size negotiation and end-of-message acknowledgement (FR-TP-031).

TP.DT (Data Transfer) frames carry the segmented payload for both flavors, sequence-numbered from 1 (FR-TP-032). Every session runs on its own actor-owned state and its own set of `IDeadline`s (T1, T2, T3, T4, Tr, Th — FR-TP-033), so multiple sessions can execute in parallel over the same physical bus (FR-TP-034/035) without interfering with each other.

The channel is an assembly-level MVP (version `0.1.0`, `IsPackable=false`) shipped alongside the other `CanKit.Pro.*` L2/L3 building blocks. It re-uses:

- `CanKit.Pro.RawCan` — one `ICanBusService` per channel to demultiplex the TP.CM / TP.DT frames back out of the shared bus stream and to confirm outbound frames.
- `CanKit.Pro.Actor` — one `IProtocolActor` mailbox for single-writer session state.
- `CanKit.Pro.Reliability` — `IDeadlineScheduler` for T1/T2/T3/T4/Tr/Th, cancelled/re-armed on the actor's loop.
- `CanKit.Pro.Addressing` — `J1939Id` / `J1939Pgn` for composing the TP.CM (PGN 0xEC00) and TP.DT (PGN 0xEB00) 29-bit IDs.

## Basic usage

```csharp
using CanKit.Abstractions.API.Can;
using CanKit.Core;
using CanKit.Pro.J1939Tp;

// One bus, two nodes on their own source addresses.
using var bus = CanBus.Open("virtual://demo/0",
    cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

using var sender   = J1939Tp.Open(bus, sourceAddress: 0x01);
using var receiver = J1939Tp.Open(bus, sourceAddress: 0x02);

// TP.BAM: broadcast to every node.
await sender.SendBamAsync(pgn: 0xFECA, payload: new byte[100]);

// TP.CM: point-to-point with RTS/CTS/EOM.
await sender.SendCmAsync(pgn: 0xFECB, destinationAddress: 0x02, payload: new byte[300]);

var received = await receiver.ReceiveAsync();
// received.Pgn / received.SourceAddress / received.DestinationAddress / received.Payload
```

## Coverage

| Requirement | Status |
| --- | --- |
| FR-TP-030 (TP.BAM send/receive) | Yes |
| FR-TP-031 (TP.CM RTS/CTS/EndOfMsgAck) | Yes |
| FR-TP-032 (TP.DT reassembly, SN 1..N) | Yes |
| FR-TP-033 (T1/T2/T3/T4/Tr/Th via DeadlineScheduler, Connection Abort) | Yes |
| FR-TP-034 (parallel sessions on shared bus) | Yes |
| FR-TP-035 (multiple concurrent TP.CM peers) | Yes |
