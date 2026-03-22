# Changelog

## 0.5.5

Published packages:

* CanKit.Abstractions 0.5.5
* CanKit.Core 0.5.5
* CanKit.Adapter.ControlCAN 0.5.5
* CanKit.Adapter.Kvaser 0.5.5
* CanKit.Adapter.PCAN 0.5.5
* CanKit.Adapter.SocketCAN 0.5.5
* CanKit.Adapter.Vector 0.5.5
* CanKit.Adapter.Virtual 0.5.5
* CanKit.Adapter.ZLG 0.5.5

### Added

* Support for **ZlgCloud** (ZlgCAN cloud devices), including device discovery and connection.
* `FrameObserved` event as the preferred replacement for `FrameReceived`, to make the `CanFrame` lifecycle clearer.

### Changed

* Improved cancellation handling in CAN bus poll loops.
* `FrameReceived` is now marked as `Obsolete` in favor of `FrameObserved`, but remains available for backward compatibility.

### Fixed

* Echo transmission in **ZLGCAN** when operating in **CAN 2.0** mode.

### Performance

* None.

### Breaking Changes

* None.

## 0.5.4

### Added

- `FaultOccurred` event for reporting unrecoverable faults.
- `CanExceptionPolicy` to standardize how adapter and receive exceptions are classified and handled.

### Changed

- Removed the duplicated and unused `ZCAN_PCIE_CANFD_200U` entry and implementation.

### Fixed

- `CancellationTokenSource` disposal when `CanBus` is disposed or a receive task stops after an exception.
- Subscriber callback isolation so exceptions in `FrameReceive` and `ErrorOccurred` handlers do not stop the receive loop.

### Performance

- None.

### Breaking Changes

- None.

## 0.5.3

### Added

- None.

### Changed

- Tightened frame-length validation across all adapters so that incoming frames cannot exceed the underlying buffer or protocol limits. Invalid frames are now handled defensively instead of propagating unexpected sizes to the application.

### Fixed

- `ArrayPoolBufferAllocator` `Memory` length: Fixed an issue where the created `Memory` slice could expose a `Length` greater than the size of the rented buffer.
- SocketCAN Classic receive payload size: Corrected the maximum application data length for Classic CAN frames in the SocketCAN receive path from 64 bytes to 8 bytes.
- Adapter receive robustness: Added length constraints to the `Receive` implementations of other adapters to prevent exceptions when the underlying interface returns malformed or oversized data.

### Performance

- None.

### Breaking Changes

- None.

## 0.5.2

### Fixed

- ZlgCAN USBCANFD bitrate in CAN 2.0 mode: Fixed an issue where the ZLGCAN USBCANFD series could not have its bitrate configured while operating in CAN 2.0 mode.
- `CanFrame` remote frame flag handling: Fixed a bug where setting the remote frame flag when constructing a `CanFrame` would overwrite other flag bits.

### Breaking Changes

- None.

## 0.5.1

### Added

- Vector device enumeration: Added helpers to query available Vector devices (filters by AppName `"CANoe"`).

### Changed

- `SocketCanBus` TX path: Optimized the send logic to reduce GC allocations and overhead.
- Adapter registration: Refactored registration patterns and entry points to leave room for upcoming Transport/Protocol layers (ISO-TP work in progress).

### Fixed

- `VectorBus` `accessMask`: Corrected the way the `accessMask` is obtained.

### Performance

- Lower allocations on transmit via the optimized `SocketCanBus` send path.

### Breaking Changes

- None.

## 0.5.0

### Added

- `CanKit.Abstractions`: New project with a corresponding NuGet package.
- Receive payload allocator: `CanBus` receive path now supports an `IBufferAllocator` for `CanFrame` payloads to optimize memory usage and reduce GC. Two default implementations are included: `ArrayPoolBufferAllocator` and `DefaultBufferAllocator`.
- Queued transmission: Introduced `QueuedCanBus` that adds a TX queue to any existing bus. Create via `ICanBus.WithQueuedTx(QueuedCanBusOptions)`.

### Changed

- Timing source: ZLG and SocketCAN adapters now use `Stopwatch` instead of `Environment.TickCount` for more stable timing.

### Performance

- Lower allocations on receive via the allocator-based payload path.
- Fewer conversions in hot paths thanks to a unified frame type (see breaking changes).

### Breaking Changes

- Unified frame type: Removed `ICanFrame`, `CanClassicFrame`, and `CanFdFrame`. Introduced a single `CanFrame` for all CAN frame kinds. Create frames using `CanFrame.Classic(...)`, `CanFrame.Fd(...)`, or `CanFrame.Create(...)`.

## 0.4.0

### Added

- `Vector` and `ControlCAN` adapters are now supported.
- ZLG: Automatic detection of the hardware auto-send/throughput limit to prevent oversend scenarios.

### Changed

- Reworked the background async read task for better efficiency and stability.
- Reduced value-copy costs for several method parameters to cut unnecessary allocations and CPU usage.

### Fixed

- Eliminated a race condition when starting and stopping the background read task during initialization/shutdown.

### Breaking Changes

- None.

## 0.3.3

### Changed

- Added `MaskFilter` and `RangeFilter` enums to `CanFeature` for more precise device capability detection.

### Fixed

- Added exception handling around `Endpoint.Enumerate()` to prevent crashes when the required driver is not installed.
- Revised the criteria for software-substitute filtering on ZLG adapters to make the filtering semantics explicit.

## 0.3.2

### Added

- Query device capabilities before opening a device via `CanBus.QueryCapabilities("kvaser://0")`.
- More `Transmit`/`TransmitAsync` overloads for easier and faster sending.
- WPF Listener sample with a simple transmit dialog for quick RX/TX experimentation.

### Changed

- Updated README with examples, including the new capability query snippet.

## 0.3.0

### Added

- Fake Backend: Introduced a mock backend implementation for easier unit testing and integration simulation.
- `NativeHandle` in `ICanBus`: Allows direct access to the underlying native handle for advanced scenarios and custom native library calls.
- `uint` overload for `AccMask` in `IBusInitOptionsConfigurator` for more flexibility in bus initialization options.

### Changed

- Expanded and improved unit test coverage for better reliability and maintainability.
- Optimized ZLG adapter and SocketCAN adapter performance for faster and more stable communication.

### Fixed

- Fixed multiple issues across all adapters, improving overall stability and compatibility.

### Breaking Changes

- None. Starting from this release, the API is considered stable. Future updates will not introduce breaking changes unless explicitly noted.

## 0.2.1

### Fixed

- Fixed `VirtualBus` receive handling.
- Ensured adapters throw consistent disposal exceptions to prevent stuck listeners.
- Corrected `ZlgCanBus` `FrameReceived` behavior so subscriptions receive frames as expected.

### Performance

- Reworked SocketCAN receive loops, reducing overhead and improving throughput under load.
- Optimized Kvaser/PCAN transmit, yielding faster benchmarks; removed timeout logic from PCAN/Kvaser transceivers.

## 0.2.0

### Changed

- Adjusted public APIs to better match common usage patterns.
- Added `Custom(key, value)` to pass adapter-specific parameters directly.

### Performance

- Reduced GC pressure in transmit/receive across all adapters.
- Improved receive path for Kvaser and PCAN to increase throughput.
