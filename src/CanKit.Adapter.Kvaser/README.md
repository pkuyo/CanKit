# CanKit.Adapter.Kvaser

Kvaser CANlib adapter for CanKit. Provides a unified .NET API to Kvaser devices via CANlib.

- Repository: https://github.com/pkuyo/CanKit
- Package: `CanKit.Adapter.Kvaser`
- Depends on: `CanKit.Core`

## Requirements

- Install Kvaser CANlib drivers/runtime from Kvaser.
- Ensure CANlib is installed and the runtime is available on the machine.
- **Ensure  `CANLib.Net` is in your NuGet package sources** (can find in CANLib SDK)
简体中文
- 需要安装 Kvaser CANlib 驱动/运行库。
- 请确认目标机器已安装并能加载 CANlib 运行库。
- 请确保 CANLib.Net 在你的Nuget源中。(在CANLib SDK中可以找到)
## Install

```bash
# Core + Kvaser adapter
dotnet add package CanKit.Core
dotnet add package CanKit.Adapter.Kvaser
```

## Endpoint Formats

- `kvaser://0` (open by channel number)
- `kvaser://?ch=1` (query parameter)

## Quick Start

```csharp
using CanKit.Core;
using CanKit.Core.Definitions;

// Open Kvaser channel 0 at 500 kbps (Classical CAN)
using var bus = CanBus.Open("kvaser://0", cfg => cfg.Baud(500_000));

// Send a frame
var tx = new CanTransmitData(new CanClassicFrame(0x321, new byte[] { 0xAA, 0xBB }));
bus.Transmit(new[] { tx });

// Receive
foreach (var rx in bus.Receive(1, 100))
{
    var f = rx.CanFrame;
    Console.WriteLine($"RX 0x{f.ID:X} dlc={f.Dlc}");
}
```

## Discover Endpoints

```csharp
using CanKit.Core.Endpoints;

foreach (var ep in BusEndpointEntry.Enumerate("kvaser"))
{
    Console.WriteLine($"{ep.Title}: {ep.Endpoint}");
}
```

## Notes

- Bit timing and FD support depend on the device and CANlib capabilities.
