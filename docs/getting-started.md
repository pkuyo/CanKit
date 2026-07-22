# Getting Started

This guide walks through installing CanKit, choosing an adapter, opening a bus, and sending/receiving frames. English is the primary language for docs. If you prefer Chinese, use the Chinese README (README_CN.md) and Chinese docs when available.

## 1) Install Packages

Install the core package, plus one or more adapter packages. Example package IDs (use published IDs in your feed):

```
# Core
dotnet add package CanKit.Core

# Adapters (pick as needed)
dotnet add package CanKit.Adapter.PCAN
dotnet add package CanKit.Adapter.Kvaser
dotnet add package CanKit.Adapter.SocketCAN
dotnet add package CanKit.Adapter.ZLG
dotnet add package CanKit.Adapter.Virtual
```

CanKit.Core auto-discovers adapter assemblies from your references (via a small generated preload list). No manual registration is needed.

## 2) Install Drivers / Native Runtimes

- PCAN (Windows): install PCAN drivers + PCAN-Basic.
- Kvaser (Windows/Linux): install Kvaser CANlib (driver + SDK). Ensure `canlib` can be loaded.
- SocketCAN (Linux): enable SocketCAN and create/configure an interface (e.g., `ip link add dev can0 type can bitrate 500000; ip link set can0 up`). And install `libsocketcan`.
- ZLG (Windows): ensure `zlgcan.dll` is available in your process load path with matching bitness (x86/x64).
- Virtual: no driver needed.

> Tip: For any native DLL not found errors, check OS, bitness (x86/x64), environment PATH/LD_LIBRARY_PATH, and that the vendor SDK is installed.

## 3) Open a Bus (Endpoints)

Use endpoint strings to open a bus with a single call and configure it via the init configurator. Examples:

```csharp
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

// SocketCAN (Linux)
using var bus = CanBus.Open("socketcan://can0", cfg =>
{
    cfg.TimingClassic(500_000)
       .EnableErrorInfo()  // if you want to receive error frames
       .SetAsyncBufferCapacity(1024);
});

bus.FrameObserved += (s, rec) =>
{
    Console.WriteLine($"RX {rec.CanFrame.FrameKind} ID={rec.CanFrame.ID:X} DLC={rec.CanFrame.Dlc}");
};

// Send one classic frame
bus.Transmit(new[] { CanFrame.Classic(0x123, new byte[]{ 0x01, 0x02 }) });

// Receive synchronously (one frame, 100ms timeout)
var items = bus.Receive(1, timeOut: 100);

// Or receive asynchronously (10 frames, 500ms timeout)
var list = await bus.ReceiveAsync(10, timeOut: 500);
```

Common endpoint forms:
- PCAN: `pcan://PCAN_USBBUS1` or `pcan://?ch=PCAN_PCIBUS1`
- Kvaser: `kvaser://0` or `kvaser://?ch=0`
- SocketCAN: `socketcan://can0` or `socketcan://can0#netlink`; optional `?rcvbuf=<bytes>`
- ZLG: `zlg://USBCANFD-200U?index=0#ch1` (device index + channel)
- Virtual: `virtual://sessionId/channelId` (e.g., `virtual://alpha/0`)

## 4) Strongly-Typed Shortcuts

For convenience, a few adapters include typed open helpers:

```csharp
using CanKit.Adapter.Kvaser;
var bus = Kvaser.Open(0, cfg => cfg.TimingFd(1_000_000, 2_000_000));

using CanKit.Adapter.PCAN;
var pcan = Pcan.Open("PCAN_USBBUS1", cfg => cfg.TimingClassic(500_000));

using CanKit.Adapter.SocketCAN;
var sc = SocketCan.Open("can0", cfg => cfg.TimingClassic(500_000));
```

## 5) Filters and Software Fallbacks

Hardware filter capabilities differ by adapter. If you need a filter mode the hardware does not support, enable software fallback during init:

```csharp
cfg.SoftwareFeaturesFallBack(CanKit.Core.Definitions.CanFeature.Filters)
   .RangeFilter(0x100, 0x1FF, CanFilterIDType.Standard);
```

Notes:
- PCAN: supports range filters; mixed types or mask rules may require software fallback.
- Kvaser: supports mask filters via `canAccept`; range typically needs software fallback.
- SocketCAN: kernel can_raw filters (mask-based) for standard/extended IDs.
- ZLG: without software fallback, a single rule type (mask OR range) per channel; some models limit rule count.
- Virtual: software filter only.

## 6) Periodic Transmit

Some adapters support hardware periodic transmit. If not, use software periodic TX:

```csharp
var handle = bus.TransmitPeriodic(
    CanFrame.Classic(0x321, new byte[]{ 0xAA }),
    new PeriodicTxOptions { IntervalMs = 100 });

// later
handle.Stop();
```

- Kvaser: hardware (object buffers) when available; otherwise fallback to software.
- ZLG: uses built-in cyclic features on supported devices; otherwise fallback to software.
- PCAN/SocketCAN/Virtual: typically software periodic TX.

## 7) Error Frames and Diagnostics

Enable error info at open if you intend to subscribe to error frames:

```csharp
var bus = CanBus.Open("kvaser://0", cfg => cfg.EnableErrorInfo());

bus.ErrorFrameReceived += (s, err) =>
{
    Console.WriteLine($"Error: {err.Type} @ {err.SystemTimestamp:O}");
};

bus.BackgroundExceptionOccurred += (s, ex) =>
{
    Console.Error.WriteLine($"Background exception: {ex}");
};
```

Support and detail level vary by adapter (e.g., precise violation location vs. generic counters). Consult adapter notes.

## 8) Enumerate Endpoints

```csharp
using CanKit.Core.Endpoints;
foreach (var ep in BusEndpointEntry.Enumerate("pcan", "kvaser", "socketcan", "zlg", "virtual"))
{
    Console.WriteLine($"{ep.Title ?? ep.Endpoint} -> {ep.Endpoint}");
}
```

## 9) Protocol Stacks (L2–L4)

Beyond raw CAN, the CanKit.Pro packages build a hardened service layer (L2) and complete
protocol stacks on top: ISO-TP (L3) and UDS / CANopen / J1939 (L4). Everything below runs
without hardware on the Virtual adapter; the identical calls work on any vendor adapter.

> Status: the L2 packages (`CanKit.Pro.Actor`, `CanKit.Pro.Addressing`, `CanKit.Pro.RawCan`,
> `CanKit.Pro.Reliability`) are published (0.1.x). The L3/L4 packages
> (`CanKit.Pro.IsoTp`, `CanKit.Pro.J1939Tp`, `CanKit.Pro.Uds`, `CanKit.Pro.CANopen`,
> `CanKit.Pro.J1939`, `CanKit.Pro.Hawe`) are still experimental (`IsPackable=false`) —
> reference their projects directly for now. Each stack has a quickstart under `samples/`.

### L2 — Raw-CAN service layer

- `CanBusService` demultiplexes one `ICanBus` into any number of independent, filtered
  subscriptions (multi-protocol on one bus), each with its own bounded buffer.
- `SendConfirmed` gives a uniform TX confirmation, with or without adapter echo.
- `ProtocolActor` gives each protocol instance a single-writer mailbox loop;
  `DeadlineScheduler` arms timeouts on it; `BusStateMonitor` pushes BusState edges.

```csharp
using var service = new CanBusService(bus);
using var sub = service.Subscribe(CanIdFilter.Range(0x700, 0x7FF, CanFilterIDType.Standard));
var confirm = await service.SendConfirmed(CanFrame.Classic(0x701, new byte[] { 1 }));
```

### L3 — ISO-TP (ISO 15765-2)

Full program: `samples/CanKit.Sample.IsoTpQuickstart`.

```csharp
using var sender = IsoTp.Open(busA, IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8));
using var receiver = IsoTp.Open(busB, IsoTpEndpoint.Normal(txCanId: 0x7E8, rxCanId: 0x7E0));
var receive = receiver.ReceiveAsync(cts.Token);
await sender.SendAsync(pdu /* 1..4095+ bytes, classic CAN or CAN FD */, cts.Token);
var datagram = await receive;
```

### L4 — UDS, CANopen, J1939

UDS client (full program: `samples/CanKit.Sample.UdsQuickstart`):

```csharp
using var client = UdsClient.Create(isoTpChannel);
await client.DiagnosticSessionControlAsync(UdsSessionType.Extended, cts.Token);
var vin = await client.ReadDataByIdentifierAsync(0xF190, cts.Token);
```

CANopen node (full program: `samples/CanKit.Sample.CanOpenQuickstart`): local object
dictionary, SDO client/server (expedited/segmented/block), TPDO/RPDO mapping (static or
rewritten over SDO), NMT master/slave, heartbeat, SYNC, EMCY, node guarding.

```csharp
using var node = CanOpen.OpenNode(bus, nodeId: 0x11);
node.ObjectDictionary.AddU16(0x2000, 0x00, 0xBEEF);
node.ConfigureTpdo(1, new PdoMapping().Add(0x2000, 0x00, bitLength: 16),
    transmission: TpdoTransmission.EventTimer, eventTimerInterval: TimeSpan.FromMilliseconds(100));
```

J1939 node (full program: `samples/CanKit.Sample.J1939Quickstart`): address claiming incl.
arbitrary-address fallback, PGN send/receive (auto TP.BAM/TP.CM above 8 bytes), fixed-rate
periodic send, SPN extraction.

```csharp
using var node = J1939Node.Open(bus, new J1939NodeOptions(name));
await node.ClaimAddressAsync(0x30);
var rpm = J1939Spn.Extract(msg.Payload.Span, byteOffset: 3, startBit: 0, bitLength: 16,
    resolution: 0.125, offset: 0.0);
```

Every stack composes the same L2 services and shares the `BackgroundExceptionOccurred`
pattern for asynchronous faults; protocol errors derive from `CanKitException` with a
library-wide error code (arc42 ADR-12).
