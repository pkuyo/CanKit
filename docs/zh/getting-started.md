# 快速开始（中文）

本文介绍如何安装 CanKit、选择适配器、打开总线并完成基本的发送/接收。若需英文版文档，请参见 ../getting-started.md。

## 1）安装 NuGet 包

安装核心包，并按需安装一个或多个适配器包：

```
# Core
Dotnet add package CanKit.Core

# 适配器（按需选择）
Dotnet add package CanKit.Adapter.PCAN
Dotnet add package CanKit.Adapter.Kvaser
Dotnet add package CanKit.Adapter.SocketCAN
Dotnet add package CanKit.Adapter.ZLG
Dotnet add package CanKit.Adapter.Virtual
```

> CanKit.Core 会通过构建期自动生成的提示列表预加载并发现适配器程序集，无需手动注册。

## 2）安装驱动/本机运行库

- PCAN（Windows）：安装 PCAN 驱动与 PCAN-Basic。并确保将 CANLib.Net 添加到你的 NuGet 包源。。
- Kvaser（Windows/Linux）：安装 Kvaser CANlib（驱动 + SDK）。确保 `canlib` 可被加载。
- SocketCAN（Linux）：启用内核 SocketCAN，并创建/配置接口（如 `ip link add dev can0 type can bitrate 500000; ip link set can0 up`）。如需通过 netlink 配置，并安装 `libsocketcan`。
- ZLG（Windows）：确保 `zlgcan.dll` 可在进程的加载路径中找到，且位数与进程匹配（x86/x64）。
- Virtual：无需驱动。

> 排错提示：若提示找不到本机 DLL，请检查 OS、位数（x86/x64）、PATH/LD_LIBRARY_PATH、以及是否正确安装了厂商 SDK。

## 3）通过 Endpoint 打开总线

使用 Endpoint 字符串一键打开通道，并通过初始化配置器设置参数：

```csharp
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

// 以 SocketCAN 为例，片段 #netlink 表示启用 netlink 进行设备层配置
using var bus = CanBus.Open("socketcan://can0#netlink", cfg =>
{
    cfg.TimingClassic(500_000)
       .EnableErrorInfo()  // 如需订阅错误帧
       .SetAsyncBufferCapacity(1024);
});

bus.FrameReceived += (s, rec) =>
{
    Console.WriteLine($"RX {rec.CanFrame.FrameKind} ID={rec.CanFrame.ID:X} DLC={rec.CanFrame.Dlc}");
};

// 发送一帧经典 CAN
bus.Transmit(new[] { CanFrame.Classic(0x123, new byte[]{ 0x01, 0x02 }) });

// 同步接收（1 帧，超时 100ms）
var items = bus.Receive(1, timeOut: 100);

// 异步批量接收（10 帧，超时 500ms）
var list = await bus.ReceiveAsync(10, timeOut: 500);
```

常见 Endpoint 形式：
- PCAN：`pcan://PCAN_USBBUS1` 或 `pcan://?ch=PCAN_PCIBUS1`
- Kvaser：`kvaser://0` 或 `kvaser://?ch=0`
- SocketCAN：`socketcan://can0` 或 `socketcan://can0#netlink`；可选 `?rcvbuf=<字节数>`
- ZLG：`zlg://USBCANFD-200U?index=0#ch1`（设备索引 + 通道）
- Virtual：`virtual://sessionId/channelId`（如 `virtual://alpha/0`）

## 4）强类型便捷入口

```csharp
using CanKit.Adapter.Kvaser;
var bus = Kvaser.Open(0, cfg => cfg.TimingFd(1_000_000, 2_000_000));

using CanKit.Adapter.PCAN;
var pcan = Pcan.Open("PCAN_USBBUS1", cfg => cfg.TimingClassic(500_000));

using CanKit.Adapter.SocketCAN;
var sc = SocketCan.Open("can0", cfg => cfg.TimingClassic(500_000));
```

## 5）过滤器与软件回退

不同适配器硬件过滤能力不同；如需硬件不支持的过滤方式，可在初始化时启用软件回退：

```csharp
cfg.SoftwareFeaturesFallBack(CanKit.Core.Definitions.CanFeature.Filters)
   .RangeFilter(0x100, 0x1FF, CanFilterIDType.Standard);
```

要点：
- PCAN：支持范围过滤；混用或掩码规则通常需软件回退。
- Kvaser：支持掩码过滤（`canAccept`）；范围过滤需要软件回退。
- SocketCAN：内核 can_raw 掩码过滤，区分标准/扩展。
- ZLG：在不开启软件回退时，同一通道仅支持一种规则类型（掩码或范围），部分设备限制规则条数。
- Virtual：仅软件过滤。

## 6）周期发送

部分设备支持硬件周期发送；否则请使用软件周期发送：

```csharp
var handle = bus.TransmitPeriodic(
    CanFrame.Classic(0x321, new byte[]{ 0xAA }),
    new PeriodicTxOptions { IntervalMs = 100 });

// 停止周期发送
handle.Stop();
```

- Kvaser：优先硬件对象缓冲；不支持时自动回退为软件。
- ZLG：在支持的设备上使用内置周期发送；否则回退为软件。
- PCAN/SocketCAN/Virtual：通常使用软件周期发送。

## 7）错误帧与诊断

若要订阅错误帧，请在打开时启用：

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

各适配器在错误帧细节上的粒度有所不同（如是否包含协议违规位置等）。

## 8）枚举 Endpoint

```csharp
using CanKit.Core.Endpoints;
foreach (var ep in BusEndpointEntry.Enumerate("pcan", "kvaser", "socketcan", "zlg", "virtual"))
{
    Console.WriteLine($"{ep.Title ?? ep.Endpoint} -> {ep.Endpoint}");
}
```
