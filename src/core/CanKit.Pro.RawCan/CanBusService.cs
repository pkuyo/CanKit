using System;
using System.Collections.Generic;
using System.Threading;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Pro.RawCan
{
    /// <summary>
    /// Default <see cref="ICanBusService"/>: attaches once to <see cref="ICanBus.FrameObserved"/>
    /// and fans each observed <see cref="CanFrameView"/> out to every registered
    /// <see cref="Subscription"/> (arc42 §5.3 "Multi-Protokoll-Demux", ADR-5).
    /// </summary>
    public sealed class CanBusService : ICanBusService
    {
        /// <summary>
        /// Default per-subscription bounded buffer capacity when none is specified.
        /// (未显式指定时每路订阅的默认有界缓冲容量。)
        /// </summary>
        public const int DefaultBufferCapacity = 1024;

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
            var subscriptions = _snapshot; // volatile read; no lock, no per-frame allocation
            if (subscriptions.Length == 0) return;

            var view = e.CanFrame;
            foreach (var subscription in subscriptions)
                subscription.TryDeliver(view);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return; // idempotent

            // Detach first so no further frames are dispatched into subscriptions we're tearing
            // down (no leaked FrameObserved handler — the exact class of leak the ownership PR
            // fixed for VirtualBusHub._hubs, here for the subscription registry).
            _bus.FrameObserved -= OnFrameObserved;

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
        }
    }
}
