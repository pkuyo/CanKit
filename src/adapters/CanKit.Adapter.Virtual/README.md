# CanKit.Adapter.Virtual

Virtual in-process CAN adapter for CanKit. Useful for development, testing, and simulation without real hardware.

- Repository: https://github.com/pkuyo/CanKit
- Package: `CanKit.Adapter.Virtual`
- Depends on: `CanKit.Core`

## Install

```bash
# Core + Virtual adapter
dotnet add package CanKit.Core
dotnet add package CanKit.Adapter.Virtual
```

## Endpoint Format

- `virtual://sessionId/channelId`
  - `sessionId`: arbitrary string; channels in the same session share a virtual bus hub.
  - `channelId`: integer (>= 0).

## Quick Start

```csharp
using CanKit.Core;
using CanKit.Core.Definitions;

// Two channels in the same session can talk to each other
using var a = CanBus.Open("virtual://alpha/0", cfg => cfg.Baud(500_000));
using var b = CanBus.Open("virtual://alpha/1", cfg => cfg.Baud(500_000));

var tx = CanFrame.Classic(0x111, new byte[] { 0x01, 0x02 });
a.Transmit(tx);

foreach (var rx in b.Receive(1, 100))
{
    var f = rx.CanFrame;
    Console.WriteLine($"b got 0x{f.ID:X} dlc={f.Dlc}");
}
```

## Discover Endpoints

```csharp
using CanKit.Core.Endpoints;

foreach (var ep in BusEndpointEntry.Enumerate("virtual"))
{
    Console.WriteLine($"{ep.Title}: {ep.Endpoint}");
}
```

## Notes

- Timing and error frames are simulated; use real adapters for hardware-accurate behavior.
