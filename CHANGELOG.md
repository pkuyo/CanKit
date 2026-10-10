# Changelog

## Unreleased

### Added

* Kvaser CANlib adapter binds `canlib32` (Windows, stdcall) and `libcanlib.so` (Linux, cdecl), including cdecl notify callbacks on Linux. Opening on Linux no longer depends on the Windows `canlib32` ABI. Fake builds are unchanged.
* Vendor adapters wrap a missing or wrong-bitness native library (`DllNotFoundException` / `BadImageFormatException`) at the first Open/constructor native call as adapter-specific `CanNativeCallException` types, with `CanKitErrorCode.NativeLibraryNotFound`, the original exception retained, and a message that names the DLL and the vendor runtime. Fake builds are unchanged.

### Fixed

* ControlCAN batch `Transmit` compared the running write total to `BATCH_COUNT` (64), so a second full native batch aborted the rest of the payload (128 frames sent, remainder dropped). The short-write check now uses this `VCI_Transmit` call's return value. `VCI_Receive` marshals the receive array as `[Out]` so native fills copy back into managed memory.
* ZLG merged receive (`ZCAN_ReceiveData`, used for PCIe-CANFD) looped until the requested number of frames had arrived and never gave up. Because the merged queue is device-wide, a channel could wait for frames another channel had already taken; its receive thread then spun at 100 % CPU and kept running after `Dispose()`. The call now returns when the driver delivers nothing within the wait time.
* ZLG `Dispose()` could throw `NullReferenceException` on .NET Framework and report a spurious fault: the poll loop ran into the disposed check, treated it as a fault and stopped itself while `Dispose()` was stopping it too, so both released the same cancellation source. Disposal during a poll is now a normal exit, and the loop state is handed over atomically.
* Kvaser hardware periodic TX ignored a finite `PeriodicTxOptions.Repeat`: the object buffer kept sending until it was stopped. A finite `Repeat` now sends `Repeat` frames in total (the immediate frame counts as the first; `Repeat = 0` sends nothing), using `canObjBufSetMsgCount`. Message-count support is checked before sending, including for an initial infinite or zero count, so a later finite `Update()` remains supported. If the device rejects the message count, the buffer is released and the software scheduler (or the existing error) takes over. `Update()` programs the count before every enable, because CANlib resets it to infinite once it is used up: a new count is used as given, switching to infinite clears a count that is still stored, and an update of only the frame or period keeps the frames that are still due (derived from the elapsed time, as CANlib does not report the remaining count, so it can be off by one). The immediate frame is only counted when the driver accepted it.
* `SoftwarePeriodicTx` (software periodic TX used by PCAN, Vector, Virtual and as fallback) sent forever for `PeriodicTxOptions.Repeat = 0` and after `Update(repeatCount: 0)`. `Repeat = 0` now sends no frame, not even the `FireImmediately` one, and `Update(repeatCount: 0)` stops the schedule. A schedule created with `Repeat = 0` starts once `Update(repeatCount: n)` sets a count. The documentation of `PeriodicTxOptions.Repeat` now states this: it is the total number of frames, not the repeats after the first one. `Stop()` (and a zero count) now ends the worker at once for periods above 50 ms instead of letting it sleep until the next period, and the frame that was due is no longer sent after a stop.
* SocketCAN BCM periodic TX sent one frame for `PeriodicTxOptions.Repeat = 0` and one more frame on `Update(repeatCount: 0)`: the kernel sends a frame whenever a job is set up with `STARTTIMER`, whatever the count is. A zero count no longer starts the timer. `Repeat = 0` only registers the job, so `Update(repeatCount: n)` can start it later, and `Update(repeatCount: 0)` stops a running job without a further frame.
* PCAN FD set the data phase to the nominal bitrate when it was given as a target bitrate: `Fd(500_000, 2_000_000)` ran at 500 kbit/s in the data phase. The data phase now uses the data bitrate and data sample point, stays within the PCAN-Basic data ranges (`data_tseg1` 1..32, `data_tseg2` 1..16, `data_sjw` 1..16) and needs at least 5 time quanta. With `data_brp=1` it keeps `data_tseg2` at 2 or more, because the driver rejects 1 there. `BitTimingSolver` now moves TSEG1 so that TSEG2 stays within `Tseg2Min`..`Tseg2Max` instead of dropping the quanta count.
* Kvaser ignored `ChannelWorkMode.ListenOnly`: the channel kept acknowledging and transmitting. It is now opened with `canDRIVER_SILENT` (`canSetBusOutputControl`, before bus-on). Opening fails if the driver type cannot be read back or does not read back as silent. Channels without `canCHANNEL_CAP_SILENT_MODE` (e.g. Leaf Light v2, CANlib virtual channels) now throw `CanFeatureNotSupportedException` for ListenOnly instead of running in normal mode, because CANlib accepts the call there but ignores it.

## 0.5.6

Published packages:

* CanKit.Abstractions 0.5.6
* CanKit.Core 0.5.6
* CanKit.Adapter.ControlCAN 0.5.6
* CanKit.Adapter.Kvaser 0.5.6
* CanKit.Adapter.PCAN 0.5.6
* CanKit.Adapter.SocketCAN 0.5.6
* CanKit.Adapter.Vector 0.5.6
* CanKit.Adapter.Virtual 0.5.6
* CanKit.Adapter.ZLG 0.5.6

### Added

* None.

### Changed

* `CanReceiveData.SystemTimestamp` can now be set during object initialization.
* Reworked Windows precision delays to use safe wait handles, respond directly to cancellation, and fall back when high-resolution waitable timers are unavailable.

### Fixed

* Concurrent registration, resolution, factory lookup, and endpoint enumeration in `CanRegistry` are now synchronized and enumeration uses stable snapshots.
* SocketCAN BCM periodic transmission now owns its frame memory, releases resources after failed setup, handles finite and infinite remaining counts reliably, and aligns update, query, and delete operations with Linux BCM behavior.
* SocketCAN now falls back to static capabilities when an existing interface does not expose CAN controller mode information.
* ZLG cloud connection no longer recurses through its asynchronous entry point, cloud names use ANSI native marshalling, and bus initialization tolerates unsupported reset operations.
* ControlCAN and ZLG periodic transmission return reserved auto-send indexes when construction fails.
* ZLG infinite receive waits are preserved by the fake native backend.
* One-shot batch integration tests now receive concurrently with transmission and enforce their intended timeout.

### Performance

* None.

### Breaking Changes

* None.

### Contributors

Thanks to the following contributors for this release:

* [@dborgards](https://github.com/dborgards).

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
