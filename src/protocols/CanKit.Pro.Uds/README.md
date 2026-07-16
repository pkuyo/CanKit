# CanKit.Pro.Uds

Experimental Unified Diagnostic Services (UDS, ISO 14229-1:2020) client for CanKit.Pro. Sits
directly on top of `CanKit.Pro.IsoTp`'s `IIsoTpChannel`, so anything that speaks ISO-TP
(virtual loopback, PCAN, SocketCAN, Vector, Kvaser, ZLG, ControlCAN, ...) can be driven with
the same client.

Status: **0.1.x MVP, `IsPackable=false`**. The public surface is stable for the services
listed below; the shape of `SendRawAsync`, timing options and NRC-mapping types may still shift
before the first NuGet release.

## Service coverage (SRS FR-UDS-001..012)

| SRS ID | Service | MVP support |
|---|---|---|
| FR-UDS-001 | 0x10 DiagnosticSessionControl | Yes — `DiagnosticSessionControlAsync(UdsSessionType | byte)` |
| FR-UDS-002 | 0x22 ReadDataByIdentifier | Yes — single-DID `ReadDataByIdentifierAsync(ushort)` |
| FR-UDS-003 | 0x2E WriteDataByIdentifier | Yes — `WriteDataByIdentifierAsync(ushort, ReadOnlyMemory<byte>)` |
| FR-UDS-004 | 0x31 RoutineControl (Start/Stop/RequestResults) | Yes — `RoutineControlAsync(UdsRoutineControlType, ushort, ...)` |
| FR-UDS-005 | 0x11 ECUReset | Yes — `EcuResetAsync(UdsEcuResetType)` |
| FR-UDS-006 | 0x27 SecurityAccess (seed/key with caller-supplied algorithm) | Yes — `SecurityAccessAsync(byte, Func<byte[], byte[]>)` |
| FR-UDS-007 | 0x3E TesterPresent + keep-alive | Yes — `TesterPresentAsync(bool)` + `StartTesterPresentKeepAlive(TimeSpan?)` |
| FR-UDS-008 | P2 / P2* timing | Yes — configurable `UdsClientOptions.P2ClientMax` / `P2StarClientMax`; `UdsTimeoutException` on expiry |
| FR-UDS-009 | NRC 0x78 responsePending | Yes — client stays inside P2* while the ECU keeps replying 0x78, bounded by `MaxResponsePendingCount` |
| FR-UDS-010 | Structured NRC | Yes — `UdsNegativeResponseException` carries requested SID + raw NRC byte + named enum |
| FR-UDS-011 | Multi-DID `0x22` | Yes (SHOULD) — `ReadDataByIdentifierAsync(IReadOnlyList<ushort>)` |
| FR-UDS-012 | 0x34 / 0x36 / 0x37 upload/download | **Deferred** (COULD, out of MVP scope) |

## Quick start

```csharp
using CanKit.Core;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Pro.IsoTp;
using CanKit.Pro.Uds;
using IsoTpFactory = CanKit.Pro.IsoTp.IsoTp;

using var bus = CanBus.Open(
    "virtual://demo/0",
    cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

var endpoint = IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8);
using var isoTp = IsoTpFactory.Open(bus, endpoint);
using var uds = UdsClient.Create(isoTp, new UdsClientOptions
{
    P2ClientMax = TimeSpan.FromMilliseconds(50),
    P2StarClientMax = TimeSpan.FromSeconds(2),
});

await uds.DiagnosticSessionControlAsync(UdsSessionType.Extended);
using var _ = uds.StartTesterPresentKeepAlive();

byte[] vin = await uds.ReadDataByIdentifierAsync(0xF190);
await uds.SecurityAccessAsync(
    requestSeedLevel: 0x01,
    computeKey: seed => YourAlgorithm.ComputeKey(seed));
```

## Design notes

* One client = one tester ↔ ECU relationship. Requests are serialized through an internal
  `SemaphoreSlim` so at most one UDS transaction is on the wire (ISO 14229-1 §7.3).
* The client never buffers responses; each `ReceiveAsync` is a bounded wait derived from the
  active timing budget (P2 first, then P2* after every 0x78).
* Stray or mismatched responses received while a request is pending are silently discarded;
  the wait continues inside the *same* budget so a chatty ECU cannot extend a P2 window.
* Transport-layer failures (ISO-TP timeout, overflow, WFTmax, etc.) are re-thrown as their
  original `IsoTpException` subclasses so callers can distinguish "ECU said no" from "wire
  broken".

## Documentation

* Requirements: `docs/requirements/SRS-CanKit.md` §4.3.1
* Architecture: `docs/architecture/arc42-CanKit.md` §6.5 (e) — UDS request/response with NRC 0x78
