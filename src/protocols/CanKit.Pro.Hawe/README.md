# CanKit.Pro.Hawe

Generic HAWE extension framework for [CanKit](https://github.com/pkuyo/CanKit): a public SPI and
reference host that lets a proprietary HAWE codec module attach onto CanKit's L2 raw-CAN service
(subscriptions, TX-confirm, actor loop, deadline scheduler) without the framework itself having
any knowledge of the HAWE protocol (SRS FR-HAWE-001..005, arc42 §5 L4).

> **Legal note (SRS CON-006 / assumption A-6).** The HAWE protocol is confidential and its
> specification is not available to this project. **No** service IDs, frame layouts, session
> transitions, keys, or any other proprietary HAWE detail are shipped in this repository, this
> assembly, or any NuGet package produced from it. Only the generic extension surface required by
> `FR-HAWE-001..005` is public. Any concrete HAWE codec must be implemented in a separate,
> non-public repository against the SPI defined here.

## Status

- Pre-release (0.1.x).
- `IsPackable=false`: this framework is not published to NuGet until a private HAWE module exists
  to consume it (SRS CON-004). Consumers depend on it via `ProjectReference`.
- No HAWE-specific code included. `FR-HAWE-004`'s session skeleton is deliberately generic
  (`Idle` / `Active` / `Fault`) and applies no protocol logic.

## Public SPI

- `IHaweCodec` — the plug-in surface a private module implements. Lifetime callbacks
  (`OnAttached`, `OnFrameReceived`, `OnSessionStateChanged`, `OnDetached`) all run on the
  channel's single-writer actor loop.
- `IHaweCodecHost` — the framework side of the same contract: `SendConfirmedAsync`,
  `SetSessionState`, `ArmDeadline`, `Post`, plus direct access to the shared `ICanBusService`
  for secondary subscriptions.
- `HaweFramePattern` — a `CanIdFilter`-based frame selector (`FR-HAWE-002`). Carries no payload
  layout, no service catalogue, no HAWE-specific semantics.
- `HaweSessionState` — placeholder three-state alphabet for `FR-HAWE-004`.
- `IHaweCodecRegistry` / `HaweCodecRegistry` — in-process, name-keyed factory registry
  analogous to `IIsoTpRegister` (`FR-HAWE-001`).
- `IHaweChannel` / `HaweChannel` — the running attachment of one codec to one bus service.

## Example

```csharp
using CanKit.Core;
using CanKit.Pro.RawCan;
using CanKit.Pro.Hawe;

// Register a codec once, at startup. In production, `MyPrivateHaweCodec` lives in a
// non-public repository and is not part of CanKit.
var registry = new HaweCodecRegistry();
registry.Register("acme-hawe-v1", () => new MyPrivateHaweCodec());

// Open a bus and share one CanBusService across every protocol instance on it (same pattern
// ISO-TP and J1939-TP already use).
using var bus = CanBus.Open("virtual://demo/0", cfg =>
    cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
using var service = new CanBusService(bus);

// The framework never sees inside `MyPrivateHaweCodec`; it just delivers matching frames to it
// and offers back the L2 services via IHaweCodecHost.
using var channel = new HaweChannel(service, registry.Create("acme-hawe-v1"));
```

## Testing

The framework ships with a `FakePatternCodec` in the test project only. It is deliberately
generic (echo one CAN ID pattern, count callbacks, drive the session skeleton) and does not
implement any HAWE-specific behaviour -- it exists solely to verify the framework's plumbing on
the Virtual adapter (`FR-HAWE-001`/`FR-HAWE-002` verification criteria).

Run the framework test suite:

```
dotnet test CanKitProHawe.slnf -c Release -f net8.0
```

## References

- SRS: `docs/requirements/SRS-CanKit.md`, §4.3.4 (`FR-HAWE-001..005`), §5 `CON-006`,
  §6 `A-6`.
- arc42: `docs/architecture/arc42-CanKit.md`, §3 L4 building blocks / §5 zoom L2.
- Analogous SPI shape: `src/core/CanKit.Abstractions/SPI/Registry/Transports/IIsoTpRegister.cs`.
