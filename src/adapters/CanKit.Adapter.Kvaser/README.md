# CanKit.Adapter.Kvaser

Kvaser CANlib adapter for CanKit. Provides a unified .NET API to Kvaser devices via CANlib.

- Repository: https://github.com/pkuyo/CanKit
- Package: `CanKit.Adapter.Kvaser`
- Depends on: `CanKit.Core`

## Requirements

- Install Kvaser CANlib drivers/runtime from Kvaser (Windows and Linux).
- Windows loads `canlib32`; Linux loads `libcanlib.so`.
- Ensure CANlib is installed and the runtime is available on the machine.
简体中文
- 需要安装 Kvaser CANlib 驱动/运行库（Windows 与 Linux）。
- Windows 加载 `canlib32`，Linux 加载 `libcanlib.so`。
- 请确认目标机器已安装并能加载 CANlib 运行库。
## Install

```bash
# Core + Kvaser adapter
dotnet add package CanKit.Core
dotnet add package CanKit.Adapter.Kvaser
```

## Endpoint Formats

- `kvaser://0` (open by channel number)
- `kvaser://?ch=1` (query parameter)

## Quick Start

```csharp
using CanKit.Core;
using CanKit.Core.Definitions;

// Open Kvaser channel 0 at 500 kbps (Classical CAN)
using var bus = CanBus.Open("kvaser://0", cfg => cfg.Baud(500_000));

// Send a frame
var tx = CanFrame.Classic(0x321, new byte[] { 0xAA, 0xBB });
bus.Transmit(tx);

// Receive
foreach (var rx in bus.Receive(1, 100))
{
    var f = rx.CanFrame;
    Console.WriteLine($"RX 0x{f.ID:X} dlc={f.Dlc}");
}
```

## Bus Usage (Bus Load)

- Before calling `BusUsage()`, you must request statistics and wait briefly; or simply use `BusUsageAsync()` which handles the request + wait.

```csharp
var kbus = (CanKit.Adapter.Kvaser.KvaserBus)bus;

// Option 1: manual request + read
kbus.RequestBusUsage();
await Task.Delay(250);
var load1 = kbus.BusUsage();

// Option 2: async helper
var load2 = await kbus.BusUsageAsync();
```

## Discover Endpoints

```csharp
using CanKit.Core.Endpoints;

foreach (var ep in BusEndpointEntry.Enumerate("kvaser"))
{
    Console.WriteLine($"{ep.Title}: {ep.Endpoint}");
}
```

## Notes

- Bit timing and FD support depend on the device and CANlib capabilities.

## Receive overruns

With `EnableErrorInfo()`, `ErrorFrameReceived` reports HW/SW receive-overrun flags
on Classic CAN and CAN FD reads. The diagnostic has `Type = Controller` and
`ControllerStatus.RxOverflow`. Data frames keep their payload and echo metadata;
error frames produce one diagnostic and are not delivered as data.

All error diagnostics carry `Direction = Rx`, native flags in `RawErrorCode` and
the read timestamp in `DeviceTimeSpan`. `Frame` is null. Diagnostics are emitted
during Kvaser transceiver enumeration, before `FrameReceived` and `FrameObserved`.
Software range filtering can discard the data while retaining the diagnostic.
Only flags returned by CANlib are observable; native filtering may hide indications.

Failed optional counter queries leave `ErrorCounters = null` and the controller
level unknown, preserving overrun flags and reception. `Unknown` describes missing
controller-level information; `RxOverflow` remains a known loss observation.

Disposal may discard an in-flight frame. The receive-loop cancellation check disposes
frames it rejects; this does not cover frames already in event delivery or the async
queue. Cancellation is silent by default. Set `IgnoreOperationCanceledException` to
`false` to expose it through `BackgroundExceptionOccurred`, subject to the exception policy.

An actual native receive failure reports a channel fault and stops reception.
Dispose the failed instance and create a new one using `Kvaser.Open(...)` or
`CanBus.Open(...)`; `Reset()` does not restart reception.
Only reads already waiting are notified of the failure: later `ReceiveAsync`
calls may wait or time out instead of rethrowing the original exception.

Each flagged read is one loss indication, not an exact lost-frame count. Both
native overrun bits may describe the same loss. An unflagged read does not signal
repair; applications own latching and acknowledgment.

See [CANlib bus errors](https://kvaser.com/canlib-webhelp/page_user_guide_bus_errors.htm)
and [canstat.h](https://kvaser.com/canlib-webhelp/canstat_8h_source.htm).
