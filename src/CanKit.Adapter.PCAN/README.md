# CanKit.Adapter.PCAN

PEAK PCAN-Basic adapter for CanKit. Provides a unified .NET API to PCAN hardware on Windows via PCAN-Basic.

- Repository: https://github.com/pkuyo/CanKit
- Package: `CanKit.Adapter.PCAN`
- Depends on: `CanKit.Core`

## Requirements

- Install PEAK PCAN-Basic runtime/drivers.
- Ensure `PCANBasic.dll` can be loaded (installed in system or alongside your app).

简体中文
- 需要安装 PEAK PCAN-Basic 驱动/运行库。
- 请确认运行时可加载 `PCANBasic.dll`（系统已安装或放到程序目录）。

## Install

```bash
# Core + PCAN adapter
dotnet add package CanKit.Core
dotnet add package CanKit.Adapter.PCAN
```

## Endpoint Formats

- `pcan://PCAN_USBBUS1`
- `pcan://?ch=PCAN_PCIBUS1`
- `pcan:PCAN_USBBUS2`

## Quick Start

```csharp
using CanKit.Core;
using CanKit.Core.Definitions;

// Open a PCAN channel by name (see PCAN-Basic docs), 500 kbps
using var bus = CanBus.Open("pcan://PCAN_USBBUS1", cfg => cfg.Baud(500_000));

var tx = new CanClassicFrame(0x700, new byte[] { 0x01 });
bus.Transmit(new[] { tx });

foreach (var rx in bus.Receive(1, 100))
{
    var f = rx.CanFrame;
    Console.WriteLine($"RX 0x{f.ID:X} dlc={f.Dlc}");
}
```

## Discover Endpoints

```csharp
using CanKit.Core.Endpoints;

foreach (var ep in BusEndpointEntry.Enumerate("pcan"))
{
    Console.WriteLine($"{ep.Title}: {ep.Endpoint}");
}
```

## Notes

- Classical and FD support depend on hardware and PCAN-Basic capabilities.
