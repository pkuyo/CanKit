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
        private readonly CanBusService _service;
        private readonly Channel<CanFrameView> _channel;

        // Exactly one of these is set (fast path vs. generic predicate); both null = accept all.
        private readonly CanIdFilter? _idFilter;
        private readonly Func<CanFrameView, bool>? _predicate;

        private int _disposed;

        internal Subscription(
            CanBusService service,
            CanIdFilter? idFilter,
            Func<CanFrameView, bool>? predicate,
            int capacity)
        {
            _service = service;
            _idFilter = idFilter;
            _predicate = predicate;

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
            if (_idFilter is { } filter)
            {
                if (!filter.Matches(view)) return;
            }
            else if (_predicate is { } predicate && !predicate(view))
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
    }
}
