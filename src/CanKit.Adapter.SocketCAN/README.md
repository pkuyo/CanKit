# CanKit.Adapter.SocketCAN

SocketCAN adapter for CanKit. Provides a unified .NET API to Linux SocketCAN interfaces.

- Repository: https://github.com/pkuyo/CanKit
- Package: `CanKit.Adapter.SocketCAN`
- Depends on: `CanKit.Core`

## Requirements

- Linux with SocketCAN enabled (`can`, `vcan`, `vxcan`).
- Ensure libsocketcan/netlink is installed.  

简体中文
- 仅支持 Linux（SocketCAN）。
- 需安装 libsocketcan。

## Install

```bash
# Core + SocketCAN adapter
dotnet add package CanKit.Core
dotnet add package CanKit.Adapter.SocketCAN
```

## Endpoint Formats

- `socketcan://can0`
- `socketcan://vcan0`
- `socketcan://can0#netlink` (enable netlink-based configuration)
- `socketcan://can0?rcvbuf=1048576` (set receive buffer)

## Quick Start

```csharp
using CanKit.Core;
using CanKit.Core.Definitions;

// Open can0, set 500 kbps
using var bus = CanBus.Open("socketcan://can0", cfg => cfg.Baud(500_000));

var tx = new CanTransmitData(new CanClassicFrame(0x123, new byte[] { 1, 2, 3 }));
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

foreach (var ep in BusEndpointEntry.Enumerate("socketcan"))
{
    Console.WriteLine($"{ep.Title}: {ep.Endpoint}");
}
```

## Notes

- When using `#netlink`, the adapter may attempt to set bitrate via libsocketcan; otherwise, ensure the interface is pre-configured (e.g., `ip link set can0 up type can bitrate 500000`).
