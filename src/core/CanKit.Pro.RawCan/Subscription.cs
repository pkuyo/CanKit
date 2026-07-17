using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using CanKit.Abstractions.API.Can.Definitions;

namespace CanKit.Pro.RawCan
{
    /// <summary>
    /// Concrete <see cref="ISubscription"/>: owns a single bounded, drop-oldest channel that is
    /// its independent RX buffer (FR-RAW-011) and applies this subscription's filter on the
    /// service's dispatch hot path.
    /// </summary>
    internal sealed class Subscription : ISubscription
    {
        private sealed class FilterCriteria
        {
            public static readonly FilterCriteria AcceptAll = new(null, null);

            public CanIdFilter? IdFilter { get; }
            public Func<CanFrameView, bool>? Predicate { get; }

            public FilterCriteria(CanIdFilter? idFilter, Func<CanFrameView, bool>? predicate)
            {
                IdFilter = idFilter;
                Predicate = predicate;
            }
        }

        private readonly CanBusService _service;
        private readonly Channel<CanFrameView> _channel;

        // Swapped atomically on Reconfigure (FR-RAW-014); volatile read on the dispatch hot path.
        private volatile FilterCriteria _criteria;

        private int _disposed;

        /// <summary>
        /// The ID-range/mask filter this subscription was registered with, or null for a
        /// predicate-based or catch-all subscription. Used only by
        /// <see cref="CanBusService.FindOverlappingFilterSubscriptions"/> (FR-RAW-041) to inspect
        /// currently registered filters; not part of the public <see cref="ISubscription"/>
        /// surface. Reflects the current filter after <see cref="Reconfigure(CanIdFilter)"/>.
        /// </summary>
        internal CanIdFilter? IdFilter => _criteria.IdFilter;

        /// <summary>
        /// True once this subscription has been disposed (by itself or by the owning service).
        /// Used only by <see cref="CanBusService.FindOverlappingFilterSubscriptions"/> to exclude
        /// entries that a concurrent <see cref="Dispose"/> may still leave in the snapshot it
        /// reads (FR-RAW-041).
        /// </summary>
        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        internal Subscription(
            CanBusService service,
            CanIdFilter? idFilter,
            Func<CanFrameView, bool>? predicate,
            int capacity)
        {
            _service = service;
            _criteria = idFilter is { } filter
                ? new FilterCriteria(filter, null)
                : predicate is { } p
                    ? new FilterCriteria(null, p)
                    : FilterCriteria.AcceptAll;

            // Bounded + DropOldest gives the same non-blocking fan-out semantics AsyncFramePipe
            // uses for the L1 RX pipe (src/core/CanKit.Core/Utils/AsyncFramePipe.cs). We use a
            // local Channel rather than AsyncFramePipe<CanFrameView> so Dispose can Complete the
            // writer and end the async enumerator deterministically (FR-RAW-012) — AsyncFramePipe
            // does not expose graceful completion.
            var options = new BoundedChannelOptions(capacity)
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            };
            _channel = Channel.CreateBounded<CanFrameView>(options);
        }

        /// <inheritdoc />
        public void Reconfigure(CanIdFilter filter)
        {
            ThrowIfNotDisposed();
            Interlocked.Exchange(ref _criteria, new FilterCriteria(filter, null));
        }

        /// <inheritdoc />
        public void Reconfigure(Func<CanFrameView, bool>? predicate)
        {
            ThrowIfNotDisposed();
            Interlocked.Exchange(
                ref _criteria,
                predicate is null ? FilterCriteria.AcceptAll : new FilterCriteria(null, predicate));
        }

        /// <summary>
        /// Dispatch hot path: called by the service for every observed frame. Must never block
        /// (FR-RAW-011): the filter check is cheap and <see cref="ChannelWriter{T}.TryWrite"/> on a
        /// bounded drop-oldest channel drops this subscription's oldest buffered frame instead of
        /// stalling the dispatch loop. Writing after the channel is completed (post-dispose) simply
        /// returns false, so a racing dispatch after removal is harmless.
        /// </summary>
        /// <remarks>
        /// <paramref name="view"/> aliases the payload memory of the adapter's own (disposable)
        /// RX-lease frame: <c>ICanBus.FrameObserved</c> fires before that frame is handed to the
        /// bus's L1 <c>AsyncFramePipe</c>, which may later dispose it (pool return / reuse) —
        /// regardless of whether a subscription has read its buffered copy yet. Queuing the raw
        /// view here would let that later dispose corrupt an unread buffered frame, so we copy the
        /// payload into an independently-owned array before writing to the channel. This is a
        /// deliberate small per-matched-frame allocation: <see cref="CanFrameView"/> has no
        /// disposal/ownership contract of its own for callers to release a pooled copy, so pooling
        /// here would require a larger API change.
        /// </remarks>
        internal void TryDeliver(in CanFrameView view)
        {
            var criteria = _criteria;
            if (criteria.IdFilter is { } filter)
            {
                if (!filter.Matches(view)) return;
            }
            else if (criteria.Predicate is { } predicate && !predicate(view))
            {
                return;
            }

            var owned = new CanFrameView(view.FrameKind, view.ID, view.Data.ToArray(), view.Flags);
            _channel.Writer.TryWrite(owned);
        }

        public IAsyncEnumerable<CanFrameView> Frames => ReadAsync();

        private async IAsyncEnumerable<CanFrameView> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var reader = _channel.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var view))
                    yield return view;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return; // idempotent
            _service.Remove(this);
            _channel.Writer.TryComplete();
        }

        /// <summary>
        /// Called by <see cref="CanBusService.Dispose"/> while tearing down every outstanding
        /// subscription. Unlike <see cref="Dispose"/> it must not call back into the service
        /// registry (the service has already removed us under its lock), avoiding re-entrancy.
        /// Idempotent and safe to interleave with a concurrent user <see cref="Dispose"/>.
        /// </summary>
        internal void CompleteFromService()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _channel.Writer.TryComplete();
        }

        private void ThrowIfNotDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(ISubscription));
        }
    }
}
