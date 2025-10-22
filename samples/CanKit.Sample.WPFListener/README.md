# CanKit.Sample.WPFListener

WPF desktop sample for CanKit that discovers endpoints, inspects device capabilities, listens to CAN traffic (Classical and FD), sends single frames, applies filters, and schedules periodic transmissions.

![Preview](https://github.com/pkuyo/CanKit/blob/master/docs/pics/cankitdemo_preview1.png)

Built on `CanKit.Core` with adapters for Kvaser, PCAN, ZLG. 
Target framework is `net8.0-windows`.

## Features

- Endpoint discovery across vendors: PCAN, Kvaser, ZLG
- Custom endpoint input and capability sniffing (CAN 2.0 vs CAN FD, supported bit rates).
- Connection options dialog: protocol mode, bit rate, data bit rate (FD), optional filters.
- Start/Stop listening; log output and a live frames table (Tx/Rx, time, ID, DLC, data) with copy-to-clipboard.
- Send dialog: craft Classic or FD frames, Std/Ext IDs, DLC validation, RTR/BRS flags.
- Periodic transmit window: build a list of frames with periods, enable/disable items, run/stop as a group.
- Software fallbacks for Filters and Periodic TX when hardware lacks those capabilities.

## Requirements

- OS: Windows 10/11.
- .NET SDK: 8.0.
- For hardware adapters, install vendor runtimes/drivers on the machine:
  - PCAN: PCAN-Basic runtime (`PCANBasic.dll`).
  - Kvaser: CANlib runtime.
  - ZLG: `zlgcan.dll` (bitness must match your app; some older devices often require x86 build).
  - Virtual: no driver needed.

## Build & Run

- Visual Studio 2022+
  - Open solution: `CanKit.sln`.
  - Set startup project: `samples/CanKit.Sample.WPFListener/CanKit.Sample.WPFListener.csproj`.
  - Run (F5).

- .NET CLI
  - Build: `dotnet build`
  - Run: `dotnet run --project samples/CanKit.Sample.WPFListener`

Notes
- WPF requires Windows; running on Linux/macOS is not supported for this sample.
- Project file: `samples/CanKit.Sample.WPFListener/CanKit.Sample.WPFListener.csproj` targets `net8.0-windows` and references Kvaser, PCAN, ZLG, and Core projects.

## Using The App

1) Select an endpoint
- Click Refresh to enumerate available endpoints from installed adapters.
- Choose "Custom" to enter an endpoint string manually (see Endpoint Strings).

2) Load capabilities
- Selecting an endpoint triggers a capability query (supported protocol modes).

3) Start listening
- Click Start to open the Connection Options dialog, choose:
  - Protocol: CAN 2.0 or CAN FD
  - Bit rate (and Data bit rate for FD)
  - Optional Filters (mask or range; standard or extended IDs)
- Press OK to open the bus; logs show status and the frame grid starts updating.
- Click Stop to close the connection.

![SetFilter](https://github.com/pkuyo/CanKit/blob/master/docs/pics/cankitdemo_preview2.png)

4) Send frames
- Click “Send...” to open the single-frame dialog (Classic/FD, Std/Ext, DLC, data, RTR/BRS). Tx frames also appear in the grid with direction = Tx.

5) Periodic transmissions
- Click “Periodic...” to build a list of periodic frames (per-item enable, DLC, RTR/BRS, period ms). Click Run/Stop to control the group.
- Hardware periodic TX is used when available; otherwise the app uses software periodic TX.

![Periodic](https://github.com/pkuyo/CanKit/blob/master/docs/pics/cankitdemo_preview3.png)

Tips
- Right‑click a frame row to copy a text representation to the clipboard.

## Endpoint (Examples)

- PCAN: `pcan://PCAN_USBBUS1` or `pcan://?ch=PCAN_PCIBUS1`
- Kvaser: `kvaser://0` or `kvaser://?ch=1`
- ZLG: `zlg://USBCANFD-200U?index=0#ch1`

See adapter READMEs for details:
- `src/CanKit.Adapter.PCAN/README.md`
- `src/CanKit.Adapter.Kvaser/README.md`
- `src/CanKit.Adapter.ZLG/README.md`

## Project Structure

- Views
  - Main window: `samples/CanKit.Sample.WPFListener/MainWindow.xaml`
  - Dialogs: `Views/ConnectionOptionsDialog.xaml`, `Views/SendFrameDialog.xaml`, `Views/FilterEditorWindow.xaml`, `Views/AddFilterDialog.xaml`, `Views/PeriodicTxWindow.xaml`, `Views/AddPeriodicItemDialog.xaml`
- ViewModels
  - `ViewModels/MainViewModel.cs`: app state, commands, orchestrates services
  - `ViewModels/PeriodicViewModel.cs`, `ViewModels/FilterEditorViewModel.cs`, `ViewModels/RelayCommand.cs`, `ViewModels/ObservableObject.cs`
- Services
  - Discovery: `Services/CanKitEndpointDiscoveryService.cs`
  - Capabilities: `Services/CanKitDeviceService.cs`
  - Listener + TX + periodic: `Services/CanKitListenerService.cs`, `Services/PeriodicTxService.cs`
  - Bus state: `Services/BusState.cs` (`IBusState`)
- Models
  - `Models/FrameRow.cs`, `Models/FilterRuleModel.cs`, `Models/PeriodicItemModel.cs`, `Models/EndpointInfo.cs`, `Models/DeviceCapabilities.cs`, `Models/FixedSizeObservableCollection.cs`
- Converters
  - `Converters/BitrateConverter.cs`, `Converters/InverseBooleanToVisibilityConverter.cs`
- Resources
  - `Resources/Converters.xaml`, `Resources/icon.ico`

## Troubleshooting

- Native DLL not found
  - Ensure the vendor runtime is installed and on the load path (PCANBasic, CANlib, zlgcan), and bitness (x86/x64) matches the app.
- ZLG older devices
  - Prefer x86 build; some devices require 32-bit apps to start properly.
- Filters/Periodic not working as expected
  - The app enables software fallbacks for filters and periodic TX when the adapter lacks these hardware features.

## See Also

- Getting Started: `docs/getting-started.md`


