using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Pro.RawCan
{
    /// <summary>
    /// Default <see cref="ICanBusService"/>: attaches once to <see cref="ICanBus.FrameObserved"/>
    /// and fans each observed <see cref="CanFrameView"/> out to every registered
    /// <see cref="Subscription"/> (arc42 §5.3 "Multi-Protokoll-Demux", ADR-5), and implements
    /// <see cref="SendConfirmed"/> (arc42 §6.3, ADR-7; FR-RAW-030..034) on the same single
    /// <see cref="ICanBus.FrameObserved"/> subscription.
    /// </summary>
    public sealed class CanBusService : ICanBusService
    {
        /// <summary>
        /// Default per-subscription bounded buffer capacity when none is specified.
        /// (未显式指定时每路订阅的默认有界缓冲容量。)
        /// </summary>
        public const int DefaultBufferCapacity = 1024;

        /// <summary>
        /// Default <see cref="SendConfirmed"/> echo-wait timeout when none is specified
        /// (FR-RAW-034). (未显式指定时 <see cref="SendConfirmed"/> 等待回显的默认超时。)
        /// </summary>
        public static readonly TimeSpan DefaultConfirmTimeout = TimeSpan.FromSeconds(1);

        private readonly ICanBus _bus;

        // Guards the mutable registry and the rebuild of _snapshot; only entered on
        // subscribe/dispose (setup/teardown), never on the per-frame dispatch path. Mirrors the
        // registry-lock discipline of VirtualBusHub.Join/Detach.
        private readonly object _gate = new();
        private readonly List<Subscription> _subscriptions = new();

        // Copy-on-write snapshot read lock-free by OnFrameObserved, so the dispatch hot path takes
        // no lock and allocates nothing per frame — same reasoning as VirtualBusHub.Broadcast not
        // holding _hubsGate while delivering.
        private volatile Subscription[] _snapshot = Array.Empty<Subscription>();

        // Pending SendConfirmed calls awaiting an echo match, keyed by (ID, payload) so multiple
        // concurrent byte-identical sends are matched FIFO instead of crashing/cross-matching
        // (FR-RAW-031). Guarded by its own lock, separate from _gate, so TX-confirm churn never
        // contends with subscription registry churn (and vice versa).
        private readonly object _pendingGate = new();
        private readonly Dictionary<PendingKey, LinkedList<PendingSend>> _pending = new();

        // Guarded by _pendingGate; set once by Dispose so a racing SendConfirmed call can never
        // register a pending entry after Dispose's final sweep has already canceled everything.
        private bool _pendingDisposed;

        // Cheap lock-free fast path: skip the _pendingGate lock (and the PendingKey hashing) in
        // OnFrameObserved entirely for services where nobody has ever called SendConfirmed. Same
        // "no cost for callers who don't use the feature" discipline as the subscription snapshot.
        private int _pendingCount;

        private int _disposed;

        /// <summary>
        /// Creates a service that demultiplexes <paramref name="bus"/>. Attaches to the bus's
        /// <see cref="ICanBus.FrameObserved"/> event immediately. (创建对 <paramref name="bus"/>
        /// 进行解复用的服务，并立即挂接其 <see cref="ICanBus.FrameObserved"/> 事件。)
        /// </summary>
        public CanBusService(ICanBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _bus.FrameObserved += OnFrameObserved;
            _bus.FaultOccurred += OnFaultOccurred;
        }

        /// <inheritdoc />
        public ICanBus Bus => _bus;

        /// <inheritdoc />
        public int SubscriptionCount
        {
            get
            {
                lock (_gate)
                {
                    return _subscriptions.Count;
                }
            }
        }

        /// <inheritdoc />
        public ISubscription Subscribe(Func<CanFrameView, bool>? predicate = null, int? bufferCapacity = null)
            => AddSubscription(idFilter: null, predicate: predicate, bufferCapacity);

        /// <inheritdoc />
        public ISubscription Subscribe(CanIdFilter filter, int? bufferCapacity = null)
            => AddSubscription(idFilter: filter, predicate: null, bufferCapacity);

        /// <inheritdoc />
        public IReadOnlyList<(ISubscription First, ISubscription Second)> FindOverlappingFilterSubscriptions()
        {
            // Snapshot read, same lock-free discipline as the dispatch hot path -- this is a
            // diagnostic call, not something exercised per-frame, but there's no reason to take
            // _gate for a read when the existing snapshot already gives a consistent view.
            var subscriptions = _snapshot;
            var overlaps = new List<(ISubscription, ISubscription)>();

            for (var i = 0; i < subscriptions.Length; i++)
            {
                if (subscriptions[i].IsDisposed || subscriptions[i].IdFilter is not { } filterI) continue;
                for (var j = i + 1; j < subscriptions.Length; j++)
                {
                    if (subscriptions[j].IsDisposed || subscriptions[j].IdFilter is not { } filterJ) continue;
                    if (filterI.Overlaps(filterJ))
                        overlaps.Add((subscriptions[i], subscriptions[j]));
                }
            }

            return overlaps;
        }

        private ISubscription AddSubscription(CanIdFilter? idFilter, Func<CanFrameView, bool>? predicate, int? bufferCapacity)
        {
            var capacity = bufferCapacity ?? DefaultBufferCapacity;
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferCapacity), "Buffer capacity must be positive.");

            var subscription = new Subscription(this, idFilter, predicate, capacity);
            lock (_gate)
            {
                if (_disposed != 0)
                    throw new ObjectDisposedException(nameof(CanBusService));

                _subscriptions.Add(subscription);
                _snapshot = _subscriptions.ToArray();
            }

            return subscription;
        }

        /// <summary>
        /// Deregisters <paramref name="subscription"/> so it stops receiving frames. Called from
        /// <see cref="Subscription.Dispose"/>. Held under <see cref="_gate"/> together with
        /// <see cref="AddSubscription"/>, mirroring VirtualBusHub.Detach.
        /// </summary>
        internal void Remove(Subscription subscription)
        {
            lock (_gate)
            {
                if (_subscriptions.Remove(subscription))
                    _snapshot = _subscriptions.ToArray();
            }
        }

        private void OnFrameObserved(object? sender, CanReceiveDataView e)
        {
            // Independent of subscription dispatch below: echo frames must be checked against
            // outstanding SendConfirmed calls regardless of whether anyone also has a
            // subscription open. Guarded by the same lock-free fast path as subscriptions.
            if (e.IsEcho && Volatile.Read(ref _pendingCount) > 0)
                TryMatchEcho(e.CanFrame);

            var subscriptions = _snapshot; // volatile read; no lock, no per-frame allocation
            if (subscriptions.Length == 0) return;

            var view = e.CanFrame;
            foreach (var subscription in subscriptions)
            {
                // A subscription's filter predicate is caller-supplied and may throw. Isolate each
                // delivery so one broken predicate can never suppress delivery to the *other*
                // subscriptions for this frame, nor escape into the bus's FrameObserved multicast
                // (which would abort dispatch to every subscription still pending in this loop) —
                // that would violate the independence every subscription is guaranteed under
                // FR-RAW-010. There is currently no fault channel on ICanBusService to surface this
                // to the caller; swallowing here is the least-bad option until one exists.
                try
                {
                    subscription.TryDeliver(view);
                }
                catch
                {
                    // ignored: see remark above.
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return; // idempotent

            // Detach first so no further frames are dispatched into subscriptions we're tearing
            // down (no leaked FrameObserved handler — the exact class of leak the ownership PR
            // fixed for VirtualBusHub._hubs, here for the subscription registry).
            _bus.FrameObserved -= OnFrameObserved;
            _bus.FaultOccurred -= OnFaultOccurred;

            Subscription[] outstanding;
            lock (_gate)
            {
                outstanding = _subscriptions.ToArray();
                _subscriptions.Clear();
                _snapshot = Array.Empty<Subscription>();
            }

            // Complete each channel outside the lock (CompleteFromService does not re-enter the
            // registry), so a slow subscriber can never turn teardown into a lock convoy.
            foreach (var subscription in outstanding)
                subscription.CompleteFromService();

            // Cancel every outstanding SendConfirmed call rather than leaving it to time out on
            // its own -- otherwise disposing the service while sends are in flight would make
            // their tasks hang until each one's individual timeout, not "no leaked resources"
            // (same reasoning as unwinding subscriptions above; standard .NET convention is that
            // disposing an in-flight operation's owner cancels it, hence TrySetCanceled rather
            // than a TxConfirmation result -- there's no SRS-defined FailureReason for "disposed").
            PendingSend[] pending;
            lock (_pendingGate)
            {
                // Set before clearing, under the same lock SendWithEchoConfirmAsync checks before
                // registering: closes the race where a call passes SendConfirmed's eager disposed
                // check but hasn't registered yet -- it now either registers-and-transmits fully
                // before this line runs (and gets swept up below like any other pending entry), or
                // sees _pendingDisposed=true and throws ObjectDisposedException instead of silently
                // leaving an orphaned entry that would otherwise sit unmatched until its own timeout.
                _pendingDisposed = true;
                pending = _pending.Values.SelectMany(list => list).ToArray();
                // Null out Node before clearing: SendWithEchoConfirmAsync's `finally` still calls
                // RemovePending once its WaitForPendingAsync unblocks below, and RemovePending's
                // "already removed" no-op check (Node == null) is what stops it from decrementing
                // _pendingCount a second time for the entries we're resolving right here.
                foreach (var p in pending)
                    p.Node = null;
                _pending.Clear();
                Volatile.Write(ref _pendingCount, 0);
            }
            foreach (var p in pending)
                p.Tcs.TrySetCanceled();
        }

        /// <inheritdoc />
        public async Task<TxConfirmation> SendConfirmed(CanFrame frame, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(CanBusService));

            var effectiveTimeout = timeout ?? DefaultConfirmTimeout;
            if (effectiveTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");

            // FR-RAW-030: one API, two internal strategies, chosen by whether the bus both
            // declares the hardware capability *and* has actually enabled it for this session --
            // CanFeature.Echo alone only means "this adapter type is capable of it", exactly like
            // every other CanFeature flag; WorkMode == Echo is the existing cross-adapter opt-in
            // that turns real echo delivery on for a given bus (see VirtualBusHub.Broadcast).
            var useEcho = _bus.Options.Features.HasFlag(CanFeature.Echo)
                          && _bus.Options.WorkMode == ChannelWorkMode.Echo;

            return useEcho
                ? await SendWithEchoConfirmAsync(frame, effectiveTimeout, cancellationToken).ConfigureAwait(false)
                : await SendApproximatedAsync(frame, cancellationToken).ConfigureAwait(false);
        }

        private async Task<TxConfirmation> SendApproximatedAsync(CanFrame frame, CancellationToken cancellationToken)
        {
            // FR-RAW-032: best-effort approximation -- confirmed as soon as the driver accepts the
            // frame, explicitly marked IsApproximated so callers can never mistake this for a real
            // hardware acknowledgment.
            var accepted = await _bus.TransmitAsync(frame, cancellationToken).ConfigureAwait(false);
            return accepted > 0
                ? new TxConfirmation { Confirmed = true, IsApproximated = true, Timestamp = DateTime.UtcNow, FailureReason = TxConfirmFailureReason.None }
                : new TxConfirmation { Confirmed = false, IsApproximated = false, Timestamp = DateTime.UtcNow, FailureReason = TxConfirmFailureReason.Rejected };
        }

        private async Task<TxConfirmation> SendWithEchoConfirmAsync(CanFrame frame, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var pending = new PendingSend(new PendingKey(frame.ID, frame.Data));

            int accepted;
            try
            {
                // Register and transmit as one atomic step under _pendingGate: this is what makes
                // TryMatchEcho's FIFO order equal actual transmission order rather than mere
                // registration order. Without it, two threads sending byte-identical frames could
                // register in one order but transmit in the other, so the oldest *pending* entry
                // is not necessarily the oldest *sent* one -- an echo could then confirm the wrong
                // caller, or confirm a send before it was even transmitted (FR-RAW-031). A
                // synchronous echo delivered inside Transmit itself (e.g. Virtual's
                // WorkMode=Echo) re-enters this same lock on this same thread -- Monitor is
                // reentrant, so TryMatchEcho can only ever see this thread's own just-registered
                // entry at that point, never a different, unrelated in-flight one. This also
                // closes the dispose race: Dispose sets _pendingDisposed under this same lock, so
                // a call can never register after Dispose has already swept and canceled every
                // pending entry -- it throws ObjectDisposedException instead, matching the eager
                // check at the top of SendConfirmed. The lock is held only across the register +
                // enqueue step (Transmit is expected to be a fast, non-blocking enqueue, same
                // assumption every other caller of ICanBus.Transmit already makes), never across
                // the echo wait, so unrelated sends are not serialized against each other.
                lock (_pendingGate)
                {
                    if (_pendingDisposed)
                        throw new ObjectDisposedException(nameof(CanBusService));

                    RegisterPending(pending);
                    accepted = _bus.Transmit(in frame);
                }
            }
            catch
            {
                RemovePending(pending);
                throw;
            }

            if (accepted <= 0)
            {
                RemovePending(pending);
                return new TxConfirmation { Confirmed = false, IsApproximated = false, Timestamp = DateTime.UtcNow, FailureReason = TxConfirmFailureReason.Rejected };
            }

            try
            {
                return await WaitForPendingAsync(pending, timeout, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // No-op if TryMatchEcho/OnFaultOccurred/Dispose already removed it; defensive
                // cleanup for the timeout/cancellation paths, which don't remove it themselves.
                RemovePending(pending);
            }
        }

        private static async Task<TxConfirmation> WaitForPendingAsync(PendingSend pending, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Registration fires on whichever comes first: caller cancellation or our own timeout.
            using var registration = timeoutCts.Token.Register(static state =>
            {
                var (p, ct) = ((PendingSend, CancellationToken))state!;
                if (ct.IsCancellationRequested)
                {
                    p.Tcs.TrySetCanceled(ct);
                }
                else
                {
                    p.Tcs.TrySetResult(new TxConfirmation
                    {
                        Confirmed = false,
                        IsApproximated = false,
                        Timestamp = DateTime.UtcNow,
                        FailureReason = TxConfirmFailureReason.Timeout,
                    });
                }
            }, (pending, cancellationToken));

            timeoutCts.CancelAfter(timeout);
            return await pending.Tcs.Task.ConfigureAwait(false);
        }

        private void RegisterPending(PendingSend pending)
        {
            lock (_pendingGate)
            {
                if (!_pending.TryGetValue(pending.Key, out var list))
                {
                    list = new LinkedList<PendingSend>();
                    _pending[pending.Key] = list;
                }
                pending.Node = list.AddLast(pending);
                Interlocked.Increment(ref _pendingCount);
            }
        }

        private void RemovePending(PendingSend pending)
        {
            lock (_pendingGate)
            {
                var node = pending.Node;
                if (node is null) return; // already removed by a match, fault, or dispose

                var list = node.List;
                list?.Remove(node);
                pending.Node = null;
                Interlocked.Decrement(ref _pendingCount);

                if (list is { Count: 0 })
                    _pending.Remove(pending.Key);
            }
        }

        private void TryMatchEcho(in CanFrameView echoView)
        {
            var key = new PendingKey(echoView.ID, echoView.Data);
            PendingSend? matched = null;

            lock (_pendingGate)
            {
                if (_pending.TryGetValue(key, out var list) && list.First is { } node)
                {
                    matched = node.Value;
                    list.RemoveFirst(); // FIFO: oldest pending send for this key matches first
                    matched.Node = null;
                    Interlocked.Decrement(ref _pendingCount);

                    if (list.Count == 0)
                        _pending.Remove(key);
                }
            }

            // TrySetResult outside the lock: never invoke TCS continuations while holding a lock.
            matched?.Tcs.TrySetResult(new TxConfirmation
            {
                Confirmed = true,
                IsApproximated = false,
                Timestamp = DateTime.UtcNow,
                FailureReason = TxConfirmFailureReason.None,
            });
        }

        private void OnFaultOccurred(object? sender, Exception ex)
        {
            // Scoped strictly to FR-RAW-033's named "Bus-Off" failure mode: FaultOccurred fires for
            // other fault severities too (see CanBusExceptionDispatcher), which aren't necessarily
            // a reason to fail every outstanding confirmation -- BusState is the authoritative
            // signal for whether this specific fault means the bus actually went off.
            if (_bus.BusState != BusState.BusOff) return;

            PendingSend[] pending;
            lock (_pendingGate)
            {
                pending = _pending.Values.SelectMany(list => list).ToArray();
                // See the identical comment in Dispose(): null Node before clearing so the
                // pending SendWithEchoConfirmAsync calls' own `finally`-triggered RemovePending
                // no-ops instead of double-decrementing _pendingCount.
                foreach (var p in pending)
                    p.Node = null;
                _pending.Clear();
                Volatile.Write(ref _pendingCount, 0);
            }

            foreach (var p in pending)
            {
                p.Tcs.TrySetResult(new TxConfirmation
                {
                    Confirmed = false,
                    IsApproximated = false,
                    Timestamp = DateTime.UtcNow,
                    FailureReason = TxConfirmFailureReason.BusOff,
                });
            }
        }
    }
}
