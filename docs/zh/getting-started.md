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

bus.FrameObserved += (s, rec) =>
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

## 9）协议栈（L2–L4）

在原始 CAN 之上，CanKit.Pro 系列包构建了加固的服务层（L2）以及完整的协议栈：
ISO-TP（L3）和 UDS / CANopen / J1939（L4）。以下全部内容都可以在无硬件的 Virtual
适配器上运行；同样的调用方式在任何厂商适配器上都适用。

> 状态：L2 包（`CanKit.Pro.Actor`、`CanKit.Pro.Addressing`、`CanKit.Pro.RawCan`、
> `CanKit.Pro.Reliability`）已发布（0.1.x）。L3/L4 包（`CanKit.Pro.IsoTp`、
> `CanKit.Pro.J1939Tp`、`CanKit.Pro.Uds`、`CanKit.Pro.CANopen`、`CanKit.Pro.J1939`、
> `CanKit.Pro.Hawe`）仍为实验性质（`IsPackable=false`）——目前请直接引用相应项目。
> 每个协议栈在 `samples/` 下都有快速上手示例。

### L2 —— 原始 CAN 服务层

- `CanBusService` 把一个 `ICanBus` 解复用为任意多个相互独立、带过滤条件的订阅
  （同一总线上并存多种协议），每个订阅拥有独立的有界缓冲。
- `SendConfirmed` 提供统一的发送确认，无论适配器是否支持硬件回显。
- `ProtocolActor` 为每个协议实例提供单写者邮箱循环；`DeadlineScheduler` 在其上
  装备超时；`BusStateMonitor` 推送总线状态边沿变化。

```csharp
using var service = new CanBusService(bus);
using var sub = service.Subscribe(CanIdFilter.Range(0x700, 0x7FF, CanFilterIDType.Standard));
var confirm = await service.SendConfirmed(CanFrame.Classic(0x701, new byte[] { 1 }));
```

### L3 —— ISO-TP（ISO 15765-2）

完整示例：`samples/CanKit.Sample.IsoTpQuickstart`。

```csharp
using var sender = IsoTp.Open(busA, IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8));
using var receiver = IsoTp.Open(busB, IsoTpEndpoint.Normal(txCanId: 0x7E8, rxCanId: 0x7E0));
var receive = receiver.ReceiveAsync(cts.Token);
await sender.SendAsync(pdu /* 1..4095+ 字节，经典 CAN 或 CAN FD */, cts.Token);
var datagram = await receive;
```

### L4 —— UDS、CANopen、J1939

UDS 客户端（完整示例：`samples/CanKit.Sample.UdsQuickstart`）：

```csharp
using var client = UdsClient.Create(isoTpChannel);
await client.DiagnosticSessionControlAsync(UdsSessionType.Extended, cts.Token);
var vin = await client.ReadDataByIdentifierAsync(0xF190, cts.Token);
```

CANopen 节点（完整示例：`samples/CanKit.Sample.CanOpenQuickstart`）：本地对象字典、
SDO 客户端/服务器（快速/分段/块传输）、TPDO/RPDO 映射（静态或通过 SDO 改写）、
NMT 主/从、心跳、SYNC、EMCY、节点守护。

```csharp
using var node = CanOpen.OpenNode(bus, nodeId: 0x11);
node.ObjectDictionary.AddU16(0x2000, 0x00, 0xBEEF);
node.ConfigureTpdo(1, new PdoMapping().Add(0x2000, 0x00, bitLength: 16),
    transmission: TpdoTransmission.EventTimer, eventTimerInterval: TimeSpan.FromMilliseconds(100));
```

J1939 节点（完整示例：`samples/CanKit.Sample.J1939Quickstart`）：地址声明（含
Arbitrary-Address 回退）、PGN 收发（超过 8 字节自动走 TP.BAM/TP.CM）、定速率周期
发送、SPN 提取。

```csharp
using var node = J1939Node.Open(bus, new J1939NodeOptions(name));
await node.ClaimAddressAsync(0x30);
var rpm = J1939Spn.Extract(msg.Payload.Span, byteOffset: 3, startBit: 0, bitLength: 16,
    resolution: 0.125, offset: 0.0);
```

所有协议栈都组合同一套 L2 服务，并通过统一的 `BackgroundExceptionOccurred` 模式
上报异步故障；协议异常都派生自 `CanKitException`，带有全库统一的错误码
（arc42 ADR-12）。
