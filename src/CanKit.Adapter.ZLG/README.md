# CanKit.Adapter.ZLG

ZLG adapter for CanKit. Provides a unified .NET API to access ZLG USBCAN/PCIe devices (CAN and CAN FD) via `zlgcan.dll`.

- Repository: https://github.com/pkuyo/CanKit
- Package: `CanKit.Adapter.ZLG`
- Depends on: `CanKit.Core`

## Requirements

- Windows with ZLG drivers installed (zlgcan runtime, typically installed with ZLGCAN or USBCAN FD package).
- Ensure `zlgcan.dll` is available on PATH or next to your app.

简体中文
- Windows，需要已安装周立功 ZLG CAN 驱动（含 `zlgcan.dll`）。
- 请保证运行时能加载到 `zlgcan.dll`（放到程序目录或加入 PATH）。

## Install

```bash
# Core + ZLG adapter
dotnet add package CanKit.Core
dotnet add package CanKit.Adapter.ZLG
```

## Endpoint Formats

- `zlg://USBCANFD-200U?index=0#ch1`
- `zlg://ZCAN_USBCANFD_200U?index=0#ch1`
- `zlg://ZLG.ZCAN_USBCANFD_200U?index=0#ch1`

Notes
- `index` selects the device index (0-based).
- `#chX` selects the channel index (e.g., `#ch0`, `#ch1`).

中文说明
- `index` 为设备索引（从 0 开始）。
- 片段部分 `#chX` 表示通道索引（如 `#ch0`、`#ch1`）。

## Quick Start

```csharp
using CanKit.Core;
using CanKit.Core.Definitions;

// Open device USBCANFD-200U, device index 0, channel 1; set CAN FD 500k/2M
using var bus = CanBus.Open(
    "zlg://USBCANFD-200U?index=0#ch1",
    cfg => cfg.Fd(500_000, 2_000_000)
);

// Transmit a classic CAN frame (ID 0x123)
var tx = new CanTransmitData(new CanClassicFrame(0x123, new byte[] { 1, 2, 3 }));
bus.Transmit(new[] { tx });

// Receive with timeout (ms)
foreach (var rx in bus.Receive(1, 100))
{
    var f = rx.CanFrame;
    Console.WriteLine($"RX id=0x{f.ID:X}, dlc={f.Dlc}");
}
```

## Discover Endpoints

You can enumerate discoverable endpoints when drivers are present:

```csharp
using CanKit.Core.Endpoints;

foreach (var ep in BusEndpointEntry.Enumerate("zlg"))
{
    Console.WriteLine($"{ep.Title}: {ep.Endpoint}");
}
```

## Notes

- Configure bitrate either via `cfg.Baud(...)` for Classical CAN or `cfg.Fd(abit, dbit, ...)` for CAN FD.
- Actual bitrate acceptance and timing depend on device and driver capability.
- For application packaging, include `zlgcan.dll` if it is not globally installed.
