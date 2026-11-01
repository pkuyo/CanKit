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
* USBCAN1/2 等 SJA1000 设备使用硬件掩码过滤；USBCAN-E-U/2E-U/4E-U 使用硬件范围过滤。若设备不支持所需规则，请启用**软件兜底**，例如：

```csharp
using var bus = CanBus.Open("controlcan://USBCAN2?index=0#ch0", cfg =>
    cfg.SoftwareFeaturesFallBack(CanFeature.RangeFilter)
       .RangeFilter(0x100, 0x200));
```

### 过滤参数与软件兜底

* SJA1000 设备的 `AccMask` 保留厂商寄存器格式：标准 ID 左移 21 位，扩展 ID 左移 3 位；掩码位 `1` 表示忽略，`0` 表示比较。例如 `AccMask(0x24600000U, 0x001FFFFFU, CanFilterIDType.Standard)` 匹配标准 ID `0x123`。多个规则转到软件时仍使用相同的输入格式，适配器负责转换。
* 仅支持硬件范围过滤的设备，其软件掩码使用普通 CAN ID，掩码位 `1` 表示比较。例如启用 `MaskFilter` 软件兜底后，`AccMask(0x123, 0x7FF, CanFilterIDType.Standard)` 匹配标准 ID `0x123`。需要跨设备一致的 ID 区间配置时，可使用 `RangeFilter` 并启用 `RangeFilter` 软件兜底。
* SJA1000 的单条硬件掩码与软件兜底均按 32 位单滤波模式解释。标准帧还可比较 RTR（位 20）和前两个数据字节（位 15..8、7..0）；扩展帧可比较 RTR（位 2）。远程帧或短帧中不存在的数据字节不参与比较；标准帧位 19..16、扩展帧位 1..0 是未使用位，始终忽略。硬件路径将这些未使用掩码位设为 `1`。无效 ID 类型在 `VCI_InitCAN` 前被拒绝。
* 原生 `VCI_INIT_CONFIG.Filter = 1` 表示单滤波模式，并不表示标准帧类型。适配器为标准和扩展 ID 均选择该模式，并在软件中检查请求的 ID 类型，排除另一帧格式的寄存器值碰撞。`CanFilterIDType.Double/Single` 是 `Standard/Extend` 的数值别名，不能用来单独选择原生双滤波模式。
* 软件兜底必须显式启用：多个硬件掩码需要 `MaskFilter`；仅支持范围过滤的设备使用掩码也需要 `MaskFilter`；仅支持掩码的设备使用范围需要 `RangeFilter`。混合列表按 OR 整体在软件中执行，只需授权不支持的规则类型（或多个掩码的硬件限制），不必重复授权同一列表中原本可由硬件处理的规则。显式 `SoftwareFilterRules` 本身就是软件配置，不受此硬件兜底开关限制。缺少授权时，在原生通道初始化之前抛出配置异常。
* 若自定义 Provider 同时声明硬件 `MaskFilter` 和 `RangeFilter`，单条掩码与范围表仍不能组合成硬件 OR。两种规则各自可用、但组合不可用时，混合列表必须启用 `SoftwareFeaturesFallBack(CanFeature.Filters)`（即同时授权两种过滤兜底）；缺少任一标志都会在初始化前被拒绝。
* USBCAN-8E-U 的范围规则需要启用 `SoftwareFeaturesFallBack(CanFeature.RangeFilter)` 后在软件中执行；本绑定不假定未验证的硬件滤波表命令。E-U/2E-U/4E-U 的滤波表状态按设备类型、索引、通道在进程内记录，关闭并重建设备对象不会丢弃未成功清除的标记。没有已知残留表的通道无需先发送清除命令；存在残留表时，必须成功清除后才能重新打开。此记录不跨进程，也无法识别其他程序写入的表。
* 关闭通道或在初始化后打开失败（包括启动接收循环失败）时，适配器会停止接收循环、复位通道并尝试清除范围表；清除失败后，下次打开仍必须重试。过滤规则在打开时固定；在已初始化的总线上调用 `ApplyConfigAfterInit` 会抛出 `CanBusConfigurationException`，不会修改硬件或软件规则。更改过滤规则需要关闭并重新打开通道。
* 打开时先复位，再写入并激活范围表，最后启动通道；不依赖固件在复位后保留范围表。
* `Dispose()` 最多等待接收任务 500 ms；若回调仍在执行，或从接收回调内调用 `Dispose()`，原生通道清理与设备租约释放会延迟到接收任务真正退出后。此期间重复打开同一通道会失败，其他通道不受影响。回调应及时返回；清理失败会记录包含设备类型、索引和通道的警告，并保留原始异常。已知残留滤波表的清除错误不会降级为仅警告后继续接收，以免旧表静默丢弃流量。
* 正在进行的发送（包括调用者提供的帧序列枚举）及其他原生操作也会保留通道和设备租约，直到调用返回或抛出异常。`Dispose()` 不等待这些调用；接收任务退出且所有原生操作结束后才执行清理。释放开始后拒绝新的原生操作，旧周期发送对象也不能修改重新打开的通道。
* 异常策略判定为 Fault 时，也会自动进入同一释放流程，无需等待用户再次调用 `Dispose()`；旧实例不可再用于操作重新打开的通道。`FaultOccurred` 回调返回、接收任务退出且清理完成后，通道才可重新打开。
* `FilterRules` 内部按 OR 组合；显式提供的 `SoftwareFilterRules` 作为额外的过滤阶段，与前一阶段按 AND 组合。即使 `FilterRules` 为空，软件规则也会生效（此前会被忽略）。此阶段由适配器对每个到达该阶段的帧执行一次，Core 不会再次执行；应用层无需重复安装相同过滤器。软件兜底不会修改调用者的任何规则列表。
* 自定义软件谓词抛出异常时，当前帧被丢弃并释放，每次异常均通过回调异常策略报告，与其他接收回调一致，不单独限流。默认继续接收后续帧；对持续失败的谓词，可将 `SubscriberCallbackSeverity` 配置为 Fault，在首次异常时停止接收并释放通道。内置收发器逐帧惰性创建托管缓冲区，尚未枚举的原生批次条目无需释放帧缓冲区。
* 整组规则无法由硬件表达时，硬件接收过滤器保持开放，所有总线流量先进入接收队列，再由软件筛选。适配器会记录此切换；高负载下仍需考虑驱动缓冲容量。

寄存器格式及 E-U/2E-U/4E-U 的过滤命令参见 [ControlCAN 使用手册](https://www.zlg.cn/data/upload/software/Can/CAN-gqsysc-V3.12.pdf) 的 2.5.5、2.6.3、2.6.8 节。单滤波的 RTR、数据字节及未使用位语义参见 [SJA1000 数据手册](https://www.nxp.com/docs/en/data-sheet/SJA1000.pdf) 的 6.4.15.1 节、图 9–10。

### 其他说明

* 周期发送的 `Stop()` 或 `Dispose()` 若未能关闭硬件条目，会记录警告并保留停止重试状态及索引；总线仍打开时可再次调用任一方法。停止和更新互斥；成功停止后不再允许更新，成功释放后重复 `Dispose()` 不会影响复用该索引的新发送任务。
* 关闭或发生终止接收故障时，会在复位与释放设备前逐条关闭已编程的硬件周期发送，不依赖 `ResetCAN` 清空自动发送表。此前停止失败的条目仍会重试关闭；关闭失败会记录警告。旧周期发送对象不能操作重新打开的通道。
* 若接收任务在设备租约交接前已发生终止故障，打开操作会抛出带故障消息的 `CanBusCreationException`，而不会成功返回已停止的总线。尚未完成的清理继续持有租约直到接收任务与原生操作结束。
* 不支持读取总线使用率（bus load）。
* `ErrorCounters()` 通过 `VCI_ReadErrInfo` 读取；可用性和准确性可能因设备/驱动而异。
* `ClearBuffer()` 清空原生及托管接收缓冲区，并丢弃已读入当前批次但尚未交付的帧，不复位或停止 CAN 通道。从接收回调内调用也有效；已开始的回调及此前已交给调用者的帧无法撤回。
* `FrameReceived` 和 `FrameObserved` 在帧进入异步接收队列前同步执行。若回调期间发生 `ClearBuffer()` 或 `Dispose()`，已开始的回调仍可读到该帧，但它不会再进入异步队列。回调中的帧及数据为借用内容，不应释放；需要在回调返回后使用时，请在回调内复制数据。清理会等待当前回调返回后再释放其帧；软件谓词返回后也会重新检查取消状态，避免关闭后继续触发帧回调。
* 如需可调整轮询间隔：`cfg.Custom("PollingInterval", 10)`（毫秒）。
* 每轮最多处理 64 个原生接收批次，然后检查错误信息。若达到此上限，立即继续接收，不等待轮询间隔；队列排空后才进入轮询等待。
