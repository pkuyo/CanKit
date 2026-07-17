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
- **Cannot Claim** (PGN 0xEE00 broadcast from SA = 0xFE) when the preferred
  address is contested by a peer with a numerically lower (higher-priority)
  NAME (**FR-J1939-004**).
- **Request-PGN** (PGN 0xEA00) send and receive (**FR-J1939-005**).
- **Auto-routing** to J1939-TP for payloads > 8 bytes; direct 29-bit frames
  for payloads ≤ 8 bytes (**FR-J1939-006**).
- **Periodic PGN send** (**FR-J1939-007**): single-frame PGNs (≤ 8 byte)
  are dispatched through the L1 `ICanBus.TransmitPeriodic` /
  `IPeriodicTx` handle (bus-native cyclic TX where the adapter supports
  it, software fallback otherwise), so timing does not compete with the
  node's actor loop; multi-frame PGNs (> 8 byte) keep a software loop
  that opens a fresh J1939-TP session per emission. The schedule tracks
  the node's SAE J1939-81 claim state — a fresh claim with a new SA
  updates the emitted frame in place via `IPeriodicTx.Update`; leaving
  `Claimed` stops the periodic handle until the node claims again. The
  caller supplies the transmit period; mapping application PGNs to
  their SAE J1939-71 standard rate is the caller's responsibility (no
  PGN rate catalog is embedded).

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
