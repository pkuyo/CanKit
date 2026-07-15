# CanKit.Pro.Addressing

CAN-ID addressing helpers for [CanKit](https://github.com/pkuyo/CanKit) (arc42 "Adressierungs-
Helfer"; SRS FR-RAW-040/041): validated 11-bit/29-bit CAN ID construction and J1939 PGN/Priority/
PDU-Format/Source-Address composition and decomposition, as pure, dependency-free helper
functions — no dependency on any other CanKit package.

This generalizes logic that previously only existed as one hard-coded case inside
`IsoTpEndpoint.CreateNormalFixed` (a single fixed diagnostics PGN) into reusable helpers any
protocol layer (ISO-TP, J1939, CANopen, ...) can call directly.

```csharp
using CanKit.Pro.Addressing;

// Validated 11/29-bit ID construction (FR-RAW-040)
CanIdRange.ValidateStandard(0x7FF);   // ok
CanIdRange.ValidateStandard(0x800);   // throws ArgumentOutOfRangeException

// J1939: build a 29-bit ID from priority/PGN/source/destination
var id = J1939Id.ComposePgn(priority: 3, pgn: 0xFED9, sourceAddress: 0x17);

// J1939: decompose a received 29-bit ID
var fields = J1939Id.Decompose(id);
fields.Priority;            // 3
fields.Pgn;                 // 0xFED9
fields.SourceAddress;       // 0x17
fields.IsPdu1;               // whether PS is a destination address or a Group Extension
fields.DestinationAddress;  // null for PDU2 (broadcast-only) PGNs
```

`CanKit.Pro.RawCan`'s `CanIdFilter` also gained an `Overlaps(CanIdFilter other)` method and
`ICanBusService.FindOverlappingFilterSubscriptions()` (FR-RAW-041, Should): a diagnostic to catch
misconfigured protocol instances whose ID-range/mask subscriptions were meant to be disjoint but
overlap.

Status: pre-release (0.1.x).
