Vector XL adapter for CanKit

- Implements Vector XL Driver (vxlapi) interop via P/Invoke
- Supports classic CAN and CAN FD (tx/rx, error frames, bitrate configuration)
- Manages driver lifetime with reference-counted open/close
- Applies mask-based hardware filters with software fallbacks
- Enumerates channels via `VectorEndpoint.Enumerate()` for discovery
- Handles x86/x64 by resolving to vxlapi.dll/vxlapi64.dll at runtime

Usage

- Endpoint: `vector://<appName>/<appChannel>` (e.g., `vector://CanKit/0`).
  - `appName` maps to the Vector XL application name used for `xlOpenPort`.
  - `appChannel` maps to the channel index which is resolved to a global channel internally.
  - In FAKE builds, use `vector://virtual/0` and `vector://virtual/1` (connected pair).
- You can still configure via code using `VectorBusInitConfigurator.AppName("...").UseChannelIndex(idx)`.
- See `Vector.Open(channel, ...)` convenience API
