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
- Kvaser (Windows/Linux): install Kvaser CANlib (driver + SDK). And add `CANLib.Net` to your NuGet package sources. Ensure `canlib` can be loaded.
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

bus.FrameReceived += (s, rec) =>
{
    Console.WriteLine($"RX {rec.CanFrame.FrameKind} ID={rec.CanFrame.ID:X} DLC={rec.CanFrame.Dlc}");
};

// Send one classic frame
bus.Transmit(new[] { new CanClassicFrame(0x123, new byte[]{ 0x01, 0x02 }) });

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
    new CanClassicFrame(0x321, new byte[]{ 0xAA }),
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
