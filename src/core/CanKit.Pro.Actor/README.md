# CanKit.Pro.Actor

Generic protocol-instance actor/scheduler for [CanKit](https://github.com/pkuyo/CanKit) (arc42
§8.3, ADR-6; SRS FR-RAW-020..024): a documented, single-mailbox threading model that any protocol
layer (ISO-TP, J1939, CANopen, ...) can build on instead of hand-rolling locks, unsynchronized
`List`s, and busy-loop schedulers.

This package has **no dependency on any other CanKit package** — it is a plain, reusable
single-writer executor plus an event-driven timer queue. Protocol layers compose it; it does not
know about CAN frames, buses, or adapters.

```csharp
using CanKit.Pro.Actor;

using var actor = new ProtocolActor(); // ActorExecutionMode.DedicatedThread by default
actor.BackgroundExceptionOccurred += (_, ex) => log.Error(ex, "protocol instance failed");

// Fire-and-forget: exceptions surface via BackgroundExceptionOccurred.
actor.Post(() => channelRegistry.Add(channel));

// Request/response: exceptions surface through the returned task instead.
var count = await actor.PostAsync(() => channelRegistry.Count);

// Event-driven timeout/STmin check -- no polling, no busy loop.
using var timeout = actor.Schedule(TimeSpan.FromMilliseconds(150), () => channel.OnN_BsTimeout());
```

## Guarantees

- **One mailbox, one loop** (FR-RAW-020/021): every posted work item and every fired timer
  callback runs strictly one at a time, in order. Protocol-instance state touched only through
  `Post`/`PostAsync`/`Schedule` never needs its own lock.
- **Event-driven, not polling** (FR-RAW-022): the loop blocks on a semaphore for either new
  mailbox work or the next timer deadline, whichever comes first. An idle actor uses ~0% CPU.
- **Background exceptions have exactly one channel** (FR-RAW-023): a throwing `Post`/`Schedule`
  item is caught by the loop and raised via `BackgroundExceptionOccurred` — never thrown on some
  unrelated caller thread, never lost as an unobserved task exception. `PostAsync` failures
  surface through the returned task instead, since the caller is already positioned to observe
  them by awaiting.
- **Configurable execution context** (FR-RAW-024): `ActorExecutionMode.DedicatedThread` (default)
  pins the loop to one real `Thread` for its entire lifetime — demonstrably the same thread for
  every callback. `ActorExecutionMode.ThreadPool` is cheaper for many short-lived instances but
  does not guarantee thread affinity. `ActorExecutionMode.SynchronizationContext` marshals every
  callback onto a caller-supplied context (e.g. a UI dispatcher) via a *blocking*
  `SynchronizationContext.Send`, so work is guaranteed to have actually run by the time it's
  considered processed — including during `Dispose`'s final drain.

Disposing an actor stops it from accepting new work (`Post`/`Schedule` throw
`ObjectDisposedException`) but runs whatever was already queued to completion first, so a caller
awaiting `PostAsync` right as `Dispose` happens still gets a real result instead of hanging.
Not-yet-due `Schedule` callbacks are discarded, not fired.

**`SynchronizationContext` mode caveat**: never call `Dispose()` synchronously from the actor's
own target context thread (e.g. from inside a UI event handler on that same dispatcher) — like any
synchronous wait on work that needs that same thread to run, it can deadlock. Dispose from a
different thread, or dispatch the call asynchronously.

Status: pre-release (0.1.x).
