# CanKit.Pro.Reliability

Error/timeout infrastructure for [CanKit](https://github.com/pkuyo/CanKit) (arc42 §5.3 / ADR-11;
SRS FR-RAW-050/051): a reusable **deadline primitive** whose expiry is guaranteed to actually be
checked and fired, and a **bus-state monitor** that pushes `ICanBus.BusState` transitions to a
protocol instance — both composed on top of `CanKit.Pro.Actor`'s single-mailbox loop, so there are
no free-running timers, no busy loops, and no second background-exception channel.

This package depends only on `CanKit.Core` (for `ICanBus`/`BusState`) and `CanKit.Pro.Actor` (for
`IProtocolActor`). Every protocol instance already runs on a `ProtocolActor` (FR-RAW-020), so a
deadline is not an independent standalone timer — it is scheduled through the actor's own
event-driven timer queue, which is exactly why its expiry can never sit as inert, never-checked
data (the deep-code-review finding "Deadlines werden gepflegt, aber nie geprüft", Review §1.1
Punkt 10).

```csharp
using CanKit.Core;
using CanKit.Pro.Actor;
using CanKit.Pro.Reliability;

using var bus = CanBus.Open("virtual://demo/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
using var actor = new ProtocolActor();

// (1) React to bus degradation so a controlled TX can abort/pause and resume (FR-RAW-051).
using var monitor = new BusStateMonitor(bus, actor);
monitor.StateChanged += (_, e) =>
{
    if (e.Current.IsTransmitBlocked())   // BusOff
        AbortActiveTransmission();
    else if (!e.Current.IsDegraded() && e.Previous.IsDegraded())
        ResumeTransmission();            // recovered back to ErrActive
};

// (2) Arm a timeout for a time-bounded transition (FR-RAW-050), e.g. an ISO-TP N_Cr window.
var scheduler = new DeadlineScheduler(actor);
var deadline = scheduler.Arm(TimeSpan.FromMilliseconds(150), () => channel.OnTimeout());

// ... later, when the awaited event arrives in time:
if (deadline.Complete())
{
    // We finished before the deadline fired; onTimeout will not run.
}
// Or refresh it on each consecutive frame instead of letting it expire:
deadline.Rearm(TimeSpan.FromMilliseconds(150));
```

## Deadlines (FR-RAW-050)

- **Guaranteed to be checked, not just stored**: `onExpired` is scheduled via the actor's own
  `Schedule`, so it is dispatched and run on the loop rather than sitting as data nobody re-reads.
- **Single, race-free resolution**: a deadline is `Pending` until exactly one of *expiry*,
  `Complete()`, or `Cancel()`/`Dispose()` wins an `Interlocked` state transition; the others become
  idempotent no-ops. `Complete()` returns whether it won — a caller's answer to "did I finish
  before the deadline fired?".
- **`Rearm` best-effort semantics**: re-arming a still-`Pending` deadline disposes the old
  actor-timer handle (best-effort) and arms a new one, using a generation counter so a stale
  pre-`Rearm` timer that the actor already dispatched no-ops instead of double-firing. Mirroring the
  actor's own documented `Schedule` caveat, a `Rearm` racing an *already-in-flight* fire is
  best-effort, not linearizable.
- **Exceptions**: an exception thrown from `onExpired` propagates out of the actor's `Schedule`
  callback and surfaces through the actor's existing `BackgroundExceptionOccurred` (FR-RAW-023) —
  there is deliberately no second exception channel.
- **Actor lifetime**: disposing the owning actor implicitly stops still-pending deadlines from
  firing — the actor's `FinalDrain` discards not-yet-due `Schedule` callbacks rather than firing
  them, so a deadline that was `Pending` when the actor is disposed simply never resolves (neither
  expires nor errors). Callers needing a guaranteed resolution must track actor lifetime
  themselves. `Rearm` after the actor is disposed lets the resulting `ObjectDisposedException`
  propagate rather than swallowing it.

## Bus-state monitoring (FR-RAW-051)

- **Self-rearming poll, not a free-running timer**: `ICanBus.BusState` has no change event, and an
  adapter's `ErrorFrameReceived`/`FaultOccurred` may not fire on every transition, so the reliable
  mechanism is a poll (default 50 ms) driven through the actor's `Schedule`, staying inside the
  event-driven-actor model instead of a busy loop.
- **Low-latency hints**: `ErrorFrameReceived` and `FaultOccurred` are additionally subscribed as
  hints that `Post` an immediate out-of-band recheck (so a BusOff is seen near-instantly), without
  touching the poll timer — the self-rearming poll remains the independent reliability floor. If an
  adapter refuses these subscriptions (e.g. `AllowErrorInfo=false`), the monitor degrades cleanly to
  poll-only.
- **Edge-triggered**: `StateChanged` fires only when the newly-read state differs from the last-seen
  one, for both degrading and recovering transitions (BusOff → ErrActive matters too).
- **Loop-thread cost**: each tick reads `BusState` synchronously on the actor's loop thread; a slow
  or blocking adapter getter therefore stalls that instance's loop for the duration — a known
  tradeoff of reusing the actor (which keeps handling single-writer-safe), not a bug fixed here.
- **Lifetime**: the poll loop also stops on its own once the owning actor is disposed. `Dispose()`
  is still required (and idempotent) to detach the two bus event subscriptions, which are
  independent of the actor's lifetime.
- **Helpers**: `BusStateExtensions.IsTransmitBlocked()` (true only for `BusOff`) and
  `IsDegraded()` (true for `ErrWarning`/`ErrPassive`/`BusOff`).

## Out of scope: FR-RAW-052 (reserved/invalid protocol values)

FR-RAW-052 (a *Should*: reserved/invalid protocol values in incoming frames — e.g. reserved ISO-TP
STmin values `0x80`–`0xF0`/`0xFA`–`0xFF` — should be interpreted per-spec, as 127 ms, rather than
throwing) is intentionally **not** implemented in this package. It is protocol-codec-specific: the
correct handling lives inside the ISO-TP frame codec, not in a generic reliability primitive, and
belongs with the future ISO-TP fix (FR-TP-007, the same review finding as Review §1.1 Punkt 6).
Building a generic "reserved value" abstraction here would be speculative over-engineering, so this
package deliberately covers only FR-RAW-050 and FR-RAW-051.

Status: pre-release (0.1.x).
