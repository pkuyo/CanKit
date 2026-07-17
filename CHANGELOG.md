# Changelog

## Unreleased

### Added

* **CanKit.Pro.CANopen 0.1.0 (MVP)** — new experimental L4 package `CanKit.Pro.CANopen`
  implementing the CiA 301 Must requirements (FR-CO-001/002/003/005/006/007/008/010/011/012):
  * Local Object Dictionary with typed read/write and data-type enforcement.
  * SDO client + server (expedited ≤ 4 bytes and segmented > 4 bytes with toggle-bit).
  * NMT master + slave state machine (Start / Stop / Pre-Op / Reset).
  * Heartbeat producer + consumer-timeout event.
  * SYNC producer + consumer, EMCY encode/decode with structured receive event.
  * Static TPDO/RPDO mapping with event, event-timer and SYNC-triggered transmission.
  * Composed on the CanKit.Pro L2 pipeline (`ICanBusService`, `IProtocolActor`,
    `DeadlineScheduler`) exactly like `CanKit.Pro.IsoTp` / `CanKit.Pro.J1939Tp` /
    `CanKit.Pro.Uds`; ships with a `CanKitProCANopen.slnf` and a `canopen-ci.yml` workflow.
  * SDO block transfer (FR-CO-004 Should) and Node-Guarding (FR-CO-009 Could) are documented
    open items for a future iteration.

### Fixed

* **CanKit.Pro.CANopen** — cap segmented SDO transfer allocations by the new
  `CanOpenNodeOptions.MaxSdoTransferBytes` (default 1 MiB) so a hostile / buggy peer cannot
  drive the server-side segmented download or the client-side segmented upload response into
  an unbounded `new byte[declaredLen]` allocation via the 32-bit size field. Over-cap
  initiates now reply with the CiA 301 "out of memory" SDO abort code (`0x05040005`).
* **CanKit.Pro.CANopen** — take the `ObjectDictionary` internal lock while snapshotting an
  entry's raw value on the SDO server upload and TPDO emission paths. Previously
  `TryGet + OdEntry.GetRawValue()` read `_value` twice (once for length, once for the byte
  copy) without any coordination with a concurrent `WriteRaw`, and a mid-copy reference swap
  could either return a length/array mismatch or throw from `Buffer.BlockCopy`. Routed
  through a new `ObjectDictionary.TryReadRaw` helper.

### Breaking Changes

* Renamed `AsyncFramePipe.ExceptionOccured` → `ExceptionOccurred` (NFR-011).
* Renamed SocketCAN `ReadTImeOutMs` → `ReadTimeoutMs` (options, configurator, runtime
  accessors, and string-key config switch) (NFR-011).
* The Abstractions namespace typo `Excpetions` is removed together with legacy
  `CanKit.Transport.IsoTp`.

### Fixed

* `CanKit.Pro.J1939Tp`: accept TP.CM EndOfMsgAck after the last DT is already on the wire
  (fast Virtual-loopback race where the peer ACKs before `SendingDt`→`WaitEom`).
* `J1939TpTests.SecondRts…`: fix `WaitForCmFrameAsync` lock object race
  (`Collection was modified` while enumerating observed CM frames).

### Added

* **CanKit.Pro.J1939 0.1.0** (pre-release, not published) — application-layer SAE J1939 node MVP under `src/protocols/CanKit.Pro.J1939`. Covers SRS FR-J1939-001..006 (Must) and FR-J1939-007 (Should):
  * `IJ1939Node` / `J1939Node` factory built on top of `ICanBusService`, `IProtocolActor`, `DeadlineScheduler` and the `CanKit.Pro.Addressing` helpers, with `IsPackable=false`.
  * PGN send/receive with `J1939Id` / `J1939Pgn` / `J1939Fields` (FR-J1939-001).
  * `J1939Spn` scale/offset SPN extraction and byte-level packer (FR-J1939-002).
  * SAE J1939-81 Address Claim (PGN 0xEE00) with 250 ms arbitration window and NAME-priority arbitration (FR-J1939-003).
  * Cannot Claim Address broadcast (SA = 0xFE) and `J1939CannotClaimException` (FR-J1939-004).
  * Request-PGN (PGN 0xEA00) send/receive (FR-J1939-005).
  * Auto-routing: payloads > 8 bytes go through `CanKit.Pro.J1939Tp` (TP.BAM/TP.CM); ≤ 8 bytes take the direct single-frame path (FR-J1939-006).
  * `StartPeriodicSend` for periodic PGN transmission (FR-J1939-007 Should): every periodic PGN — single-frame and multi-frame alike — runs through the node's `SendAsync` / actor loop on top of the L2 `DeadlineScheduler` (L2 scheduling). `SendAsync`'s pre-flight claim gate runs on every tick, so the schedule stops emitting when the node leaves `Claimed` and resumes automatically after a fresh claim (the 29-bit ID is composed from the currently-claimed SA on every emission). Send failures — including `J1939NoAddressException` from the claim gate — surface via `BackgroundExceptionOccurred`. A native L1 `IPeriodicTx` optimization for single-frame PGNs is deferred until the L1 fallback path can report `Transmit` errors uniformly. The caller supplies the transmit period; no SAE J1939-71 PGN rate catalog is embedded.
* Solution/filter wiring: `CanKitProJ1939.slnf`, `CanKit.sln` entry, `tests/CanKit.Tests/CanKit.Tests.csproj` project reference, `eng/package-versions.props` `CanKitProJ1939Version` = 0.1.0.
* CI workflow `.github/workflows/j1939-ci.yml` (Ubuntu + Windows net8.0/net48) modeled on `j1939tp-ci.yml`.
* `tests/CanKit.Tests/TestCases/J1939/J1939NodeTests.cs` — Virtual-loopback integration tests covering every FR-J1939-001..006 requirement plus SPN cross-byte extraction and round-trip.

### Removed

* Legacy `CanKit.Transport.IsoTp` prototype (functionally defective; superseded by `CanKit.Pro.IsoTp`).
* Abstractions ISO-TP surface `CanKit.Abstractions.API.Transport.*` (including the `Excpetions`
  typo namespace) and `IIsoTpRegister`.
* PCAN native ISO-TP register path (`PcanIsoTp*`, `PcanIsoTpRegister`) that depended on the
  removed Abstractions transport API.
* `CanKitTransports.slnf` and `.github/workflows/transports-ci.yml` (replaced by Pro IsoTp CI).

### Changed

* ISO-TP is exclusively provided by `CanKit.Pro.IsoTp` on the L2 services.
* `docs/requirements/SRS-CanKit.md` (§4.3.3 J1939): Ist-Zustand aktualisiert (MVP umgesetzt, offene Punkte dokumentiert); Traceability-Tabelle aktualisiert.
* `docs/architecture/arc42-CanKit.md`: L4-Zeile in der Schichtenübersicht und der Umsetzungstabelle aktualisiert (J1939-App-MVP als vorhanden markiert).

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
