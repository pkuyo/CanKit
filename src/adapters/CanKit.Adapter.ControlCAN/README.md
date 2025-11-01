# CanKit.Adapter.ControlCAN

用于 CanKit 的 ControlCAN 适配器。通过 `ControlCAN.dll` 为使用 ControlCAN API 的设备（例如 GCAN/广成、CHX/创芯、ZLG/周立功 等）提供统一的 .NET API。

* 包：`CanKit.Adapter.ControlCAN`
* 依赖：`CanKit.Core`

## 系统要求

* 需要在 Windows 上安装厂商提供的 ControlCAN 运行时（`ControlCAN.dll`）。
* 确保 `ControlCAN.dll` 在系统 PATH 中，或与应用程序放在同一目录。
* 进程位数需与 DLL 匹配（x86/x64）。部分老设备只提供 32 位库；如不确定，请以 x86 为目标。

## 安装

```bash
dotnet add package CanKit.Core
dotnet add package CanKit.Adapter.ControlCAN
```

## 端点格式

* `controlcan://USBCAN2?index=0#ch1`
* `controlcan://VCI_USBCAN2?index=0#ch1`
* `controlcan://?type=USBCAN2&index=0#ch1`

## 说明

* `index` 选择设备索引（从 0 开始）。
* `#chX` 选择通道索引（例如 `#ch0`、`#ch1`）。
* `type` 映射到 ControlCAN 的设备类型。若省略，默认 `USBCAN2`。

## 快速上手

```csharp
using CanKit.Core;
using CanKit.Core.Definitions;

// 以 500 kbps（经典 CAN）打开 USBCAN2 设备索引 0 的通道 1
using var bus = CanBus.Open("controlcan://USBCAN2?index=0#ch1",
    cfg => cfg.Baud(500_000));

// 发送
bus.Transmit(CanFrame.Classic(0x321, new byte[] { 0xAA, 0xBB }));

// 接收（带超时，单位：毫秒）
foreach (var rx in bus.Receive(1, 100))
{
    var f = rx.CanFrame;
    Console.WriteLine($"RX 0x{f.ID:X} dlc={f.Dlc}");
}
```

备注

* 仅支持经典 CAN；ControlCAN 适配器不支持 CAN FD。
* 硬件过滤为**基于掩码**。若需按 ID 区间过滤，请启用**软件兜底**并添加区间规则，例如：

```csharp
using var bus = CanBus.Open("controlcan://USBCAN2?index=0#ch0", cfg =>
    cfg.SoftwareFeaturesFallBack(CanFeature.RangeFilter)
       .RangeFilter(0x100, 0x200));
```

* 不支持读取总线使用率（bus load）。
* `ErrorCounters()` 通过 `VCI_ReadErrInfo` 读取；可用性和准确性可能因设备/驱动而异。
* 如需可调整轮询间隔：`cfg.Custom("PollingInterval", 10)`（毫秒）。
