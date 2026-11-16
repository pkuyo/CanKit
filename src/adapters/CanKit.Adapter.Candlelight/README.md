# CanKit.Adapter.Candlelight

Candlelight USB-CAN adapter for CanKit using the Candle native library.

## Requirements

- Windows with a candlelight-compatible device and its WinUSB driver.
- `Candle.dll` on the native library search path or beside the application executable. The DLL is not included in this package. The DLL supplied with this workspace is x64; use a process and DLL of matching architecture.

## Usage

```csharp
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Core;

using var bus = CanBus.Open("candlelight://0/0", cfg => cfg.Baud(500_000));
bus.Transmit(CanFrame.Classic(0x123, new byte[] { 0x11, 0x22 }));
foreach (var item in bus.Receive(1, 100))
	Console.WriteLine($"0x{item.CanFrame.ID:X}");
```

Endpoints use `candlelight://<device-index>/<channel-index>`; the channel defaults to zero. `candlelight://0#ch1` is also supported. Device indexes follow the current Candle device scan order and can change when devices are connected or disconnected. Enumerating `candlelight` reports channel zero for each discovered device without opening it; additional channels can be opened explicitly.

## Capabilities

The included native API exposes classic CAN only, with up to eight data bytes. Listen-only, one-shot transmission, error reporting, and hardware timestamps depend on the channel's capabilities. Software filtering and periodic transmission are available through CanKit's fallback options. The native API does not expose CAN FD, termination control, bus state, error counters, or bus load. The configured transmit timeout is not applied by Candle.dll. Timestamp values are device-relative microseconds when the device supports hardware timestamps; otherwise they may be zero. The underlying 32-bit timestamp wraps.

Receive echo confirmations are delivered in `ChannelWorkMode.Echo`; normal and listen-only modes omit transmit confirmations. Remote frames cannot retain a nonzero requested DLC in CanKit's frame representation.
