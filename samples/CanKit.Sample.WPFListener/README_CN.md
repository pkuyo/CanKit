# CanKit.Sample.WPFListener（WPF 监听示例）

基于 WPF 的 CanKit 示例程序：支持端点发现、能力嗅探、CAN（2.0/FD）监听、单帧发送、过滤规则和周期发送等功能。目标框架为 `net8.0-windows`。

![预览](https://gitee.com/pkuyora/CanKit/raw/master/docs/pics/cankitdemo_preview1.png)

## 功能

- 多厂商端点发现：PCAN、Kvaser、ZLG、Virtual（同时枚举 SocketCAN，但本示例为 Windows WPF）。
- 自定义端点输入，设备能力嗅探（支持 CAN 2.0 / CAN FD、可用速率）。
- 连接选项对话框：协议模式、比特率、FD 数据速率，可选过滤规则。
- 启停监听；日志输出与帧表格（Tx/Rx、时间、ID、DLC、数据），支持复制到剪贴板。
- 发送对话框：构造经典或 FD 帧、标准/扩展 ID、DLC 校验、RTR/BRS 标志。
- 周期发送窗口：构建周期任务列表（按项启用/禁用），一键运行/停止。
- 当硬件不支持过滤/周期发送时，自动启用软件回退。

## 环境要求

- 操作系统：Windows 10/11。
- .NET SDK：8.0。
- 使用硬件适配器时，需要安装对应厂商运行库/驱动：
  - PCAN：PCAN-Basic（`PCANBasic.dll`）。
  - Kvaser：CANlib 运行库。
  - ZLG：`zlgcan.dll`（进程位数需匹配；部分老设备通常建议 x86）。
## 构建与运行

- Visual Studio 2022 及以上
  - 打开解决方案：`CanKit.sln`。
  - 启动项目：`samples/CanKit.Sample.WPFListener/CanKit.Sample.WPFListener.csproj`。
  - 运行（F5）。

- .NET CLI
  - 构建：`dotnet build` (不要使用Fake配置否则无法正常连接设备)
  - 运行：`dotnet run --project samples/CanKit.Sample.WPFListener`

说明
- WPF 仅支持 Windows；Linux/macOS 不适用本示例。
- 项目文件：`samples/CanKit.Sample.WPFListener/CanKit.Sample.WPFListener.csproj`（`net8.0-windows`，引用 Kvaser、PCAN、ZLG 与 Core）。

## 使用说明

1）选择端点
- 点击 Refresh 枚举已安装适配器的可用端点。
- 选择 “Custom” 可手动输入端点字符串（见下文端点格式示例）。

2）加载能力
- 选中端点即触发能力查询（支持的协议与速率）。

3）开始监听
- 点击 Start 弹出连接选项：
  - 协议：CAN 2.0 或 CAN FD
  - 比特率（FD 还需选择数据速率）
  - 可选过滤（掩码/范围；标准/扩展 ID）
- 确认后打开总线；日志区域显示状态，右侧帧表开始更新。
- 点击 Stop 停止监听并关闭连接。

![设置过滤](https://gitee.com/pkuyora/CanKit/raw/master/docs/pics/cankitdemo_preview2.png)

4）发送帧
- 点击 “Send...” 打开单帧发送窗口（经典/FD、标准/扩展、DLC、数据、RTR/BRS）。发送的 Tx 帧也会在表格中记录（方向为 Tx）。

5）周期发送
- 点击 “Periodic...” 构建周期发送列表（按项启用、DLC、RTR/BRS、周期 ms）。点击 Run/Stop 运行或停止所有启用项。
- 若硬件支持将优先使用硬件周期发送，否则自动使用软件周期发送。

![设置周期发送](https://gitee.com/pkuyora/CanKit/raw/master/docs/pics/cankitdemo_preview3.png)

提示
- 右键帧行可复制文本到剪贴板。

## Endpoint（示例）

- PCAN：`pcan://PCAN_USBBUS1` 或 `pcan://?ch=PCAN_PCIBUS1`
- Kvaser：`kvaser://0` 或 `kvaser://?ch=1`
- ZLG：`zlg://USBCANFD-200U?index=0#ch1`

更多细节参考适配器文档：
- `src/CanKit.Adapter.PCAN/README.md`
- `src/CanKit.Adapter.Kvaser/README.md`
- `src/CanKit.Adapter.ZLG/README.md`

## 项目结构

- 视图（Views）
  - 主窗口：`samples/CanKit.Sample.WPFListener/MainWindow.xaml`
  - 对话框：`Views/ConnectionOptionsDialog.xaml`、`Views/SendFrameDialog.xaml`、`Views/FilterEditorWindow.xaml`、`Views/AddFilterDialog.xaml`、`Views/PeriodicTxWindow.xaml`、`Views/AddPeriodicItemDialog.xaml`
- 视图模型（ViewModels）
  - `ViewModels/MainViewModel.cs`：应用状态与命令编排
  - `ViewModels/PeriodicViewModel.cs`、`ViewModels/FilterEditorViewModel.cs`、`ViewModels/RelayCommand.cs`、`ViewModels/ObservableObject.cs`
- 服务（Services）
  - 发现：`Services/CanKitEndpointDiscoveryService.cs`
  - 能力：`Services/CanKitDeviceService.cs`
  - 监听/发送/周期：`Services/CanKitListenerService.cs`、`Services/PeriodicTxService.cs`
  - 总线状态：`Services/BusState.cs`（`IBusState`）
- 模型（Models）
  - `Models/FrameRow.cs`、`Models/FilterRuleModel.cs`、`Models/PeriodicItemModel.cs`、`Models/EndpointInfo.cs`、`Models/DeviceCapabilities.cs`、`Models/FixedSizeObservableCollection.cs`
- 转换器（Converters）
  - `Converters/BitrateConverter.cs`、`Converters/InverseBooleanToVisibilityConverter.cs`
- 资源（Resources）
  - `Resources/Converters.xaml`、`Resources/icon.ico`

## 故障排查

- 本机找不到厂商 DLL
  - 确认已安装相应运行库，且进程位数（x86/x64）与 DLL 匹配（PCANBasic、CANlib、zlgcan）。
- ZLG 老设备
  - 建议优先使用 x86（32 位）编译与运行。
- 过滤/周期发送行为不如预期
  - 当硬件不支持，示例会采用软件回退实现过滤和周期发送。

## 参考

- 快速上手：`docs/getting-started.md`

