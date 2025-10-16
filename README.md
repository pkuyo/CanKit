# CanKit

CanKit is an unified .NET abstraction for working with multiple CAN adapters through a single, consistent API. 

It supports opening buses via endpoint strings or strongly-typed helpers, and exposes common operations (send/receive, filters, periodic TX, error monitoring) across adapters.

- Adapters: PCAN-Basic (Peak), Kvaser CANlib, SocketCAN (Linux), ZLG(周立功)
- Targets: .NET Standard 2.0 and .NET 8 (incl. net8.0-windows)

For Chinese readers, see: [README_CN.md](README_CN.md)


## Features

- Unified API surface
  - Open by endpoint or typed helpers; enumerate endpoints
  - Classic CAN 2.0 and CAN FD frames
  - Sync and async I/O: `Transmit/Receive`, `TransmitAsync/ReceiveAsync`, and `GetFramesAsync` (NET 8+)
  - Events: `FrameReceived`, `ErrorFrameReceived`, `BackgroundExceptionOccurred`
- Timing and modes
  - Classic and FD bit timing helpers; advanced segment-based timing via `CanBusTiming`
  - Work modes: Normal / Listen-Only / Echo (loopback) when supported by device
- Filtering
  - Configure mask or range filters via a unified API
  - Software fallbacks when hardware filtering is limited
- Periodic transmit
  - Hardware cyclic TX where available; otherwise software periodic TX
- Diagnostics and telemetry
  - Error frames, bus state, error counters, bus usage (availability depends on adapter)

## Install

Add the core package and one or more adapter packages as needed. Package IDs are indicative; use the actual published IDs for your feed.

```
dotnet add package CanKit.Core

// Pick adapters you need
dotnet add package CanKit.Adapter.PCAN
dotnet add package CanKit.Adapter.Kvaser
dotnet add package CanKit.Adapter.SocketCAN
dotnet add package CanKit.Adapter.ZLG
dotnet add package CanKit.Adapter.Virtual
```


## Quick Start

Open a bus using an endpoint string, then send/receive frames. Examples:

- PCAN: `pcan://PCAN_USBBUS1`
- Kvaser: `kvaser://0` or `kvaser://?ch=0`
- SocketCAN (Linux): `socketcan://can0#netlink`
- ZLG: `zlg://USBCANFD-200U?index=0#ch1`
- Virtual: `virtual://alpha/0`

```csharp
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

// Open via endpoint; configure timing/work mode/etc. via the configurator
using var bus = CanKit.Core.CanBus.Open(
    "socketcan://can0#netlink",
    cfg => cfg.TimingClassic(500_000));

// Subscribe for frames (enable error info at open if you need error frames)
bus.FrameReceived += (s, rec) =>
{
    Console.WriteLine($"RX {rec.CanFrame.FrameKind} ID={rec.CanFrame.ID:X} DLC={rec.CanFrame.Dlc}");
};

// Transmit a classic CAN frame
var frame = new CanClassicFrame(0x123, new byte[] { 0x11, 0x22, 0x33 });
bus.Transmit(new[] { frame });

// Receive (sync)
var items = bus.Receive(1, timeOut: 100);

// Receive (async batch)
var list = await bus.ReceiveAsync(10, timeOut: 500);
```

Prefer a strongly-typed helper? Available for some adapters:

```csharp
using CanKit.Adapter.Kvaser;
var kvaser = Kvaser.Open(0, cfg => cfg.TimingFd(1_000_000, 2_000_000));
```



## Endpoints & Enumeration

Enumerate discoverable endpoints:

```csharp
using CanKit.Core.Endpoints;

foreach (var ep in BusEndpointEntry.Enumerate("pcan", "kvaser", "socketcan", "zlg", "virtual"))
{
    Console.WriteLine($"{ep.Title ?? ep.Endpoint} -> {ep.Endpoint}");
}
```


## Getting Started

Read the step-by-step guide, including installing drivers/runtime libraries per adapter:

- English: [getting-started](docs/getting-started.md)


## Adapter Notes (Quick)

- PCAN (Windows): install PCAN drivers and PCAN-Basic; the native PCANBasic.dll must be loadable (x86/x64 must match your process).
- Kvaser (Windows/Linux): install Kvaser Driver + CANlib; ensure canlib is loadable and channel accessible.
- SocketCAN (Linux): enable SocketCAN, configure the interface (ip link...), and install libsocketcan for netlink-based config (`#netlink`).
- ZLG (Windows): ensure zlgcan.dll is present and bitness matches your process. It is **strongly recommended** to compile as an x86 application. For some older devices (e.g., USBCAN1/2), not enabling this may prevent the device from starting properly.
- Virtual: no driver needed.


## Behavior Differences (Heads-up)

Different adapters may handle timeouts, filters, error frames, and periodic TX differently:

- Timeouts: some adapters ignore TX timeouts (e.g., PCAN, Kvaser), others honor both TX/RX timeouts.
- Filters: mask vs. range support varies; software fallback can be enabled when hardware filtering is limited.
- Error Frames/Counters/Bus Usage: availability depends on adapter/driver.
- Periodic TX: hardware-backed on some adapters; otherwise use software periodic TX fallback.

See docs for detailed, adapter-specific notes.


## Project Status

This library is under active development. Due to limited access to hardware, not every feature is fully validated across all device families and OS combinations. If you hit issues, please open an issue. Contributions (PRs), device-specific fixes, and test reports are very welcome!


## License

This project is available under the LICENSE in this repository.
