# CanKit.Adapter.Vector

- Implements Vector XL Driver (vxlapi) interop via P/Invoke
- Supports Classic CAN and CAN FD (tx/rx, error frames, bitrate configuration)
- Manages driver lifetime with reference-counted open/close
- Applies mask-based hardware filters with software fallbacks
- Enumerates channels via `VectorEndpoint.Enumerate()` for discovery
- Handles x86/x64 by resolving to vxlapi.dll/vxlapi64.dll at runtime

## Requirements

- Windows with Vector XL Driver installed (vxlapi runtime).
- Ensure `vxlapi.dll`/`vxlapi64.dll` are discoverable via PATH or placed beside your app.

## Install

```bash
dotnet add package CanKit.Core
dotnet add package CanKit.Adapter.Vector
```

## Endpoint Formats

- `vector://<appName>/<channelIndex>` (e.g., `vector://CanKit/0`)
  - `<appName>` is the Vector XL application name used by `xlOpenPort`.
  - `<channelIndex>` maps to the app channel index; internally resolved to global channel.
- FAKE builds: `vector://virtual/0`, `vector://virtual/1` (connected pair).

## vQuick Start

```csharp
using CanKit.Core;
using CanKit.Core.Definitions;

// Open Vector channel 0 with CAN FD 500k/2M
using var bus = CanBus.Open("vector://CanKit/0", cfg => cfg.Fd(500_000, 2_000_000));

// Transmit
bus.Transmit(new CanClassicFrame(0x123, new byte[] { 1, 2, 3 }));

// Receive (timeout in ms)
foreach (var rx in bus.Receive(1, 100))
{
    var f = rx.CanFrame;
    Console.WriteLine($"RX id=0x{f.ID:X}, dlc={f.Dlc}");
}
```

## Bus State & Error Counters

- VectorBus updates `BusState` and `ErrorCounters` upon receiving a chip-state event.
- Call `RequestBusState()` first, then wait briefly before reading:

```csharp
var vbus = (CanKit.Adapter.Vector.VectorBus)bus;
vbus.RequestBusState();
await Task.Delay(200); // allow vxlapi to deliver state event
var state = vbus.BusState;
var counters = vbus.ErrorCounters();
```

## Notes

- Hardware filters are mask-based; range filters can be emulated via software fallback if enabled.
- If `xlOpenPort` returns no configuration permission, initial timing and output mode may not be applied.
- Bus usage (bus load) is not supported via this adapter.
- x86/x64 runtime is auto-resolved (`vxlapi.dll` / `vxlapi64.dll`).
