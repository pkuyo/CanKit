# CanKit



[![Nuget](https://img.shields.io/nuget/v/CanKit.Core.svg?logo=nuget)](https://www.nuget.org/packages/CanKit.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CanKit.Core.svg?logo=nuget)](https://www.nuget.org/packages/CanKit.Core)
[![.NET Standard 2.0](https://img.shields.io/badge/.NET%20Standard-2.0-512BD4?logo=dotnet&logoColor=white)](#)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](#)

[![CI-adapter-kvaser](https://github.com/pkuyo/CanKit/actions/workflows/kvaser-ci.yml/badge.svg)](https://github.com/pkuyo/CanKit/actions/workflows/kvaser-ci.yml)
[![CI-adapter-pcan](https://github.com/pkuyo/CanKit/actions/workflows/pcan-ci.yml/badge.svg)](https://github.com/pkuyo/CanKit/actions/workflows/pcan-ci.yml)
[![CI-adapter-socketcan](https://github.com/pkuyo/CanKit/actions/workflows/socketcan-ci.yml/badge.svg)](https://github.com/pkuyo/CanKit/actions/workflows/socketcan-ci.yml)
[![CI-adapter-virtual](https://github.com/pkuyo/CanKit/actions/workflows/virtual-ci.yml/badge.svg)](https://github.com/pkuyo/CanKit/actions/workflows/virtual-ci.yml)

**CanKit** 是一个统一的、跨平台的、支持多厂商的高性能 c# CAN 通信库。

支持通过 Endpoint 字符串或强类型入口打开总线，并在不同适配器上提供尽可能一致的收发、过滤、周期发送、错误帧与诊断体验。

 - 适配厂商：PCAN-Basic (Peak CAN), CANlib(Kvaser), SocketCAN (Linux), ZLG(周立功)

 ----

 下面是一个使用CanKit开发的CAN通信Demo，支持多厂商发送接收，查询连接设备，设置滤波等功能。[CAN通信Demo](https://github.com/pkuyo/CanKit/tree/master/samples/CanKit.Sample.WPFListener) 

![预览](https://gitee.com/pkuyora/CanKit/raw/master/docs/pics/cankitdemo_preview1.png)

## 安装

先添加核心包，再按需选择适配器包：

```
# Core
dotnet add package CanKit.Core

# Adapters（按需选择）
dotnet add package CanKit.Adapter.PCAN
dotnet add package CanKit.Adapter.Kvaser
dotnet add package CanKit.Adapter.SocketCAN
dotnet add package CanKit.Adapter.ZLG
dotnet add package CanKit.Adapter.Virtual
```

> 注：驱动/运行库需按各厂商要求单独安装（见下方“适配器说明”）。


## 特性（Features）

- 高性能与背压
  - 高吞吐流水线，背压友好的异步接口
  - 可配置缓冲容量，避免过载与丢帧
- 统一 API
  - Endpoint 打开或强类型打开；支持枚举 Endpoint
  - 支持经典 CAN 2.0 与 CAN FD 帧类型
  - 同步/异步：`Transmit/Receive`、`TransmitAsync/ReceiveAsync`、`GetFramesAsync`（.NET 8+）
  - 事件：`FrameReceived`、`ErrorFrameReceived`、`BackgroundExceptionOccurred`
- 定时与模式
  - 经典/FD 位时序配置；支持段参数（Tseg1/Tseg2/Brp 等）
  - 工作模式：正常/只听/回环（取决于设备能力）
- 过滤器
  - 统一的掩码/范围过滤配置接口
  - 当硬件能力有限时，可启用软件回退
- 周期发送
  - 支持设备上的硬件周期发送（若可用）；否则自动使用软件周期发送
- 诊断与监控
  - 错误帧、总线状态、错误计数、总线利用率（是否可用取决于适配器）
- 运行时选项
  - 异步缓冲容量、接收循环延迟停止、发送重试策略（单次/重试）、启用错误信息等


## 快速开始

通过 Endpoint 打开，然后发送/接收（示例Endpoint）：

- PCAN: `pcan://PCAN_USBBUS1`
- Kvaser: `kvaser://0` or `kvaser://?ch=0`
- SocketCAN (Linux): `socketcan://can0#netlink`
- ZLG: `zlg://USBCANFD-200U?index=0#ch1`
- Virtual: `virtual://alpha/0`

### 发送 & 接收
```csharp
// 快速上手：打开总线，发送一帧，带超时接收
using CanKit.Core;
using CanKit.Core.Definitions;

// 通过端点打开；在配置器中设置比特率等参数
using var bus = CanBus.Open(
    "socketcan://can0#netlink",
    cfg => cfg.TimingClassic(500_000)); // 经典 CAN 500 kbps

// 同步发送一帧经典 CAN
var frame = new CanClassicFrame(0x123, new byte[] { 0x11, 0x22, 0x33 });
var sentCount = bus.Transmit(frame);

// 异步发送同一帧
sentCount = await bus.TransmitAsync(frame);

// 同步接收（最多 1 帧），单位为毫秒
var items = bus.Receive(1, timeOut: 100);

// 异步批量接收（最多 10 帧），带超时
var list = await bus.ReceiveAsync(10, timeOut: 500);
```
### 事件驱动接收 + 批量发送
```csharp
// 事件驱动接收、友好打印，及批量发送
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using System;

// 打开总线；在配置器中按需设置比特率/模式
using var bus = CanBus.Open(
    "socketcan://can0#netlink",
    cfg => cfg.TimingClassic(500_000));

// 订阅接收事件（如需错误/状态帧，请在配置中开启）
bus.FrameReceived += (_, rec) =>
{
    var f = rec.CanFrame;
    Console.WriteLine($"RX {f.FrameKind} ID=0x{f.ID:X} DLC={f.Dlc}");
};

// 单帧发送（同步 + 异步）
var frame = new CanClassicFrame(0x123, new byte[] { 0x11, 0x22, 0x33 });
var sentCount = bus.Transmit(frame);
sentCount = await bus.TransmitAsync(frame);

// 批量发送（同步 + 异步）
var frames = new[] { frame, frame, frame, frame };
sentCount = bus.Transmit(frames);
sentCount = await bus.TransmitAsync(frames);

// 可选：按需拉取（同步/异步）并设置超时
var one = bus.Receive(1, timeOut: 100);
var many = await bus.ReceiveAsync(10, timeOut: 500);

```
若更偏好强类型入口：

```csharp
using CanKit.Adapter.Kvaser;
var bus = Kvaser.Open(0, cfg => cfg.TimingFd(1_000_000, 2_000_000));
```


## Endpoint 与枚举

```csharp
using CanKit.Core.Endpoints;
foreach (var ep in BusEndpointEntry.Enumerate("pcan", "kvaser", "socketcan", "zlg", "virtual"))
{
    Console.WriteLine($"{ep.Title ?? ep.Endpoint} -> {ep.Endpoint}");
}
```

## 查询设备支持功能
```csharp
using CanKit.Core;

var capability = CanBus.QueryCapabilities("kvaser://0");
Console.WriteLine($"support: {capability.Features}")
```

## 适配器说明

- PCAN（Windows）：安装 PCAN 驱动与 PCAN-Basic；确保 `PCANBasic.dll` 可加载（进程位数匹配 x86/x64）。
- Kvaser（Windows/Linux）：安装 Kvaser Driver + CANlib；确保 canlib 可加载并能访问通道。
- SocketCAN（Linux）：启用内核 SocketCAN，创建/配置接口（`ip link …`）；安装 `libsocketcan`。
- ZLG（Windows）：确保 `zlgcan.dll` 在加载路径，且位数与进程匹配。**强烈建议**编译为x86程序，对于一部分老设备（USBCAN1/2等）不开启会导致无法正常开启设备。
- Virtual：无需驱动。


## 行为差异

- 超时语义：部分适配器对 TX 超时不生效（如 PCAN, Kvaser）；SocketCAN 对 TX/RX 超时均支持；ZLG 的 RX 超时传入底层。
- 过滤器：各家支持的过滤类型不同（掩码/范围）；不足可启用软件回退。
- 错误帧/计数/总线利用率：可用性视适配器与驱动而定。
- 周期发送：部分设备可使用硬件周期发送；否则用软件周期发送回退。

> 详细适配器差异请参考英文文档（稍后提供中文版差异页）。


## 入门

- 中文：[快速开始](docs/zh/getting-started.md)


## 项目状态

本库仍在积极开发中。由于可用硬件有限，尚未能对所有设备型号与系统组合进行完整验证。如遇问题，欢迎提交 Issue；也非常欢迎 PR、设备适配改进与测试反馈！


## 许可

请参见仓库中的 LICENSE 文件。
