# CanKit.Pro.J1939

Application-layer SAE J1939 node for CanKit.Pro. MVP (0.1.x) covering SRS
FR-J1939-001..006 (Must) and FR-J1939-007 (Should).

## What it does

- **PGN send/receive** with 29-bit Priority / PF / PS / SA encoding &
  decoding via `CanKit.Pro.Addressing.J1939Id` (**FR-J1939-001**).
- **SPN extraction** from PGN payloads with configurable resolution and
  offset (little-endian, 1..64-bit fields) via `J1939Spn` (**FR-J1939-002**).
- **Address claiming** (PGN 0xEE00) with SAE J1939-81 §4.4.3 NAME arbitration
  and the 250 ms announcement window (**FR-J1939-003**).
- **Address-Claim fallback** (**FR-J1939-004**): after losing the preferred
  address to a higher-priority NAME the node scans the arbitrary address
  field (0x80..0xF7, wrapping once) and only broadcasts **Cannot Claim**
  (PGN 0xEE00 from SA = 0xFE) when the field is exhausted. Governed by
  `J1939NodeOptions.EnableArbitraryAddressClaiming` (default: derived from
  the NAME's Arbitrary Address Capable bit).
- **Request-PGN** (PGN 0xEA00) send and receive (**FR-J1939-005**).
- **Auto-routing** to J1939-TP for payloads > 8 bytes; direct 29-bit frames
  for payloads ≤ 8 bytes (**FR-J1939-006**).
- **Periodic PGN send** (**FR-J1939-007**): every periodic PGN — single-
  frame and multi-frame alike — is emitted on a fixed-rate grid anchored
  on the L2 `DeadlineScheduler` (t0 + n × period), so the long-run rate
  does not drift by the per-emission send time; ticks whose previous
  emission is still in flight are skipped and ticks that fell behind are
  coalesced. The schedule snapshots the caller's payload into an owned
  buffer at Start-time and hands `SendAsync` the same immutable bytes on
  every tick, so in-place edits to the caller buffer after
  `StartPeriodicSend` are not observable on the wire. `SendAsync`'s
  pre-flight claim gate runs on every emission, so the schedule stops
  putting frames on the wire as soon as the node leaves `Claimed` and
  resumes automatically after a fresh claim — the emitted 29-bit ID is
  composed from the currently-claimed SA. Send failures (including
  `J1939NoAddressException` from the claim gate) are surfaced via
  `BackgroundExceptionOccurred`. The caller supplies the transmit period;
  mapping application PGNs to their SAE J1939-71 standard rate is the
  caller's responsibility (no PGN rate catalog is embedded). A native L1
  `IPeriodicTx` optimization for single-frame PGNs remains a follow-up —
  its L1 error-propagation blocker is resolved (`IPeriodicTx.Faulted`).

## Architecture

- Composes strictly on L2 (`CanKit.Pro.RawCan.ICanBusService`,
  `CanKit.Pro.Actor.IProtocolActor`,
  `CanKit.Pro.Reliability.DeadlineScheduler`) — no vendor SDK dependency.
- Uses `CanKit.Pro.Addressing` helpers (`J1939Id`, `J1939Pgn`, `J1939Fields`,
  `J1939Name`); the node never reimplements ID / PGN / NAME math.
- Delegates multi-frame transport to `CanKit.Pro.J1939Tp` per FR-J1939-006.
- Follows the same factory / interface / impl pattern as `CanKit.Pro.Uds`
  and `CanKit.Pro.J1939Tp`.

## Usage

```csharp
var options = new J1939NodeOptions(
    new J1939Name(
        identityNumber: 0x12345,
        manufacturerCode: 0x0AB,
        ecuInstance: 0, functionInstance: 0, function: 0x81,
        reserved: false,
        vehicleSystem: 0, vehicleSystemInstance: 0,
        industryGroup: 0, arbitraryAddressCapable: false));

using var bus = CanBus.Open("virtual://demo/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20));
using var node = J1939Node.Open(bus, options);

await node.ClaimAddressAsync(preferredAddress: 0x11);

// Direct single-frame PGN (≤ 8 bytes).
await node.SendAsync(new J1939Message(pgn: 0xFEF0, payload: new byte[] { 1, 2, 3, 4 }));

// Multi-frame PGN (> 8 bytes) auto-routes through J1939-TP.
await node.SendAsync(new J1939Message(pgn: 0xFECA, payload: new byte[64]));

// Request-PGN.
await node.RequestPgnAsync(requestedPgn: 0xFEF1, destinationAddress: 0xFF);

// SPN extraction (little-endian, physical = raw * resolution + offset).
node.MessageReceived += (_, msg) =>
{
    double speed = J1939Spn.Extract(msg.Payload.Span,
        byteOffset: 3, startBit: 0, bitLength: 16,
        resolution: 0.125, offset: 0.0);
};
```

## Status

Pre-release; `IsPackable=false`. Not yet published to NuGet.
