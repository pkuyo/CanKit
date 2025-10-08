# CanKit.Core

Core abstractions and utilities for CanKit: unified CAN bus API, endpoints, frames, filters, timing, and helpers.

- Repository: https://github.com/pkuyo/CanKit
- Package: `CanKit.Core`

This package defines the common interfaces and helpers. To talk to real hardware, install an adapter package such as:
- `CanKit.Adapter.SocketCAN` (Linux)
- `CanKit.Adapter.PCAN` (PEAK PCAN-Basic)
- `CanKit.Adapter.Kvaser` (Kvaser CANlib)
- `CanKit.Adapter.ZLG` (ZLGCAN)
- `CanKit.Adapter.Virtual` (no hardware, for testing)

简体中文
- 该包提供核心抽象与工具。要访问实际硬件，请安装对应适配器包（如 SocketCAN/PCAN/Kvaser/ZLG/Virtual）。

## Install

```bash
dotnet add package CanKit.Core
```

## Quick Start

Below uses the Virtual adapter for a minimal example. Replace the endpoint with your adapter’s scheme (e.g., `socketcan://can0`, `pcan://PCAN_USBBUS1`).

```csharp
using CanKit.Core;
using CanKit.Core.Definitions;

// Open a bus by endpoint; configure bitrate at open
using var bus = CanBus.Open("virtual://demo/0", cfg => cfg.Baud(500_000));

// Transmit a classic CAN frame
var tx = new CanTransmitData(new CanClassicFrame(0x123, new byte[] { 1, 2, 3 }));
bus.Transmit(new[] { tx });

// Receive with timeout (ms)
foreach (var rx in bus.Receive(1, 100))
{
    var f = rx.CanFrame;
    Console.WriteLine($"RX id=0x{f.ID:X}, dlc={f.Dlc}");
}
```

For CAN FD, configure both arbitration/data bitrates:

```csharp
using var fdBus = CanBus.Open("virtual://demo/1", cfg => cfg.Fd(500_000, 2_000_000));
```

## Enumerate Endpoints

List endpoints exposed by installed adapters:

```csharp
using CanKit.Core.Endpoints;

foreach (var ep in BusEndpointEntry.Enumerate())
{
    Console.WriteLine($"{ep.Title}: {ep.Endpoint}");
}
```

Filter by scheme/vendor:

```csharp
foreach (var ep in BusEndpointEntry.Enumerate("socketcan", "pcan", "kvaser", "zlg", "virtual"))
{
    Console.WriteLine(ep.Endpoint);
}
```

## Notes

- Bit timing helpers: `cfg.Baud(...)` for Classical CAN, `cfg.Fd(abit, dbit, ...)` for CAN FD.
- Received frames expose `CanReceiveData.CanFrame`. Build frames with `CanClassicFrame` or other implementations.
