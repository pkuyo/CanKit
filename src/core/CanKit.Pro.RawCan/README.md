# CanKit.Pro.RawCan

Raw-CAN service layer for [CanKit](https://github.com/pkuyo/CanKit): multi-protocol
demultiplexing / subscriptions (arc42 §5.3, ADR-5; SRS FR-RAW-010..013).

One `ICanBusService` wraps one `ICanBus` and turns its single `FrameObserved` RX stream into
N independent, filtered, read-only `ISubscription`s — so several protocol instances (ISO-TP,
J1939, CANopen, …) can each see their own view of the same bus **without competing over
`ReceiveAsync`** and without one slow consumer blocking the others.

```csharp
using CanKit.Core;
using CanKit.Pro.RawCan;

using var bus = CanBus.Open("virtual://demo/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
using var service = new CanBusService(bus);

// Fast path: one 11-bit ID range per protocol instance (no per-frame delegate).
using var isoTp = service.Subscribe(CanIdFilter.Range(0x700, 0x7FF));

// Generic predicate when a range/mask is not enough.
using var custom = service.Subscribe(view => view.IsExtendedFrame && view.Len == 8);

await foreach (var frame in isoTp.Frames.WithCancellation(token))
{
    // frame is a read-only CanFrameView (no ownership/disposal concerns)
}
```

Each subscription owns its own bounded, drop-oldest buffer (FR-RAW-011). Disposing a
subscription deterministically deregisters it and completes its `Frames` stream; disposing the
service unwinds all subscriptions and detaches from the bus (FR-RAW-012). This layer is built
purely on the public `ICanBus.FrameObserved` surface, so it works identically for every adapter.

Status: pre-release (0.1.x).
