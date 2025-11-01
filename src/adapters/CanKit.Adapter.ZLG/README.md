# CanKit.Adapter.ZLG

用于 CanKit 的 ZLG 适配器。通过 `zlgcan.dll` 为周立功（ZLG）的 USBCAN/PCIe 系列设备（支持 CAN 与 CAN FD）提供统一的 .NET API。

* 代码仓库：[https://github.com/pkuyo/CanKit](https://github.com/pkuyo/CanKit)
* 包名：`CanKit.Adapter.ZLG`
* 依赖：`CanKit.Core`

## 系统要求

* Windows，已安装 ZLG 驱动（zlgcan 运行时，通常随 ZLGCAN 或 USBCAN FD 安装包一并安装）。
* 确保运行时能够加载到 `zlgcan.dll`（放在应用目录或加入系统 PATH）。
* **强烈建议**将应用编译为 x86。对部分老设备（如 USBCAN1/2），未编译为 x86 可能导致设备无法正常启动。

## 安装

```bash
# Core + ZLG 适配器
dotnet add package CanKit.Core
dotnet add package CanKit.Adapter.ZLG
```

## 端点格式

* `zlg://USBCANFD-200U?index=0#ch1`
* `zlg://ZCAN_USBCANFD_200U?index=0#ch1`
* `zlg://ZLG.ZCAN_USBCANFD_200U?index=0#ch1`

## 说明

* `index` 选择设备索引（从 0 开始）。
* `#chX` 选择通道索引（例如 `#ch0`、`#ch1`）。
* `type` 映射到 ControlCAN 的设备类型。若省略，默认 `USBCAN2`。


## 快速上手

```csharp
using CanKit.Core;
using CanKit.Core.Definitions;

// 打开设备 USBCANFD-200U，设备索引 0，通道 1；设置 CAN FD 500k/2M
using var bus = CanBus.Open(
    "zlg://USBCANFD-200U?index=0#ch1",
    cfg => cfg.Fd(500_000, 2_000_000)
);

// 发送一个经典 CAN 帧（ID 0x123）
var tx = CanFrame.Classic(0x123, new byte[] { 1, 2, 3 });
bus.Transmit(tx);

// 接收（带超时，单位毫秒）
foreach (var rx in bus.Receive(1, 100))
{
    var f = rx.CanFrame;
    Console.WriteLine($"RX id=0x{f.ID:X}, dlc={f.Dlc}");
}
```

## 支持设备 (未完全测试)

* UUSBCAN-I/I+、USBCAN-I-MINI
* USBCAN-II/II+、MiniPCIeCAN-II
* PCI-9820、PCI-9820I
* PCI-5010-U、PCI-5020-U、USBCAN-E-U、USBCAN-2E-U、USBCAN-4E-U、USBCAN-8E-U
* USBCANDTU-100UR、CANDTU-200UR
* USBCANFD-100U、USBCANFD-200U、USBCANFD-400U、USBCANFD-800U、USBCANFD-MINI
* PCIE-CANFD-100U、PCIE-CANFD-200U-EX、PCIE-CANFD-400U、M/2CANFD、MiniPCIeCANFD
* PCIE_CANFD_200U

## 备注

* 速率配置：经典 CAN 用 `cfg.Baud(...)`，CAN FD 用 `cfg.Fd(abit, dbit, ...)`。
* 实际可接受的比特率与时序取决于具体设备与驱动能力。
* 如需可调整轮询间隔：`cfg.Custom("PollingInterval", 10)`（毫秒）。
