using System;
using System.Collections.Generic;
using CanKit.Abstractions.API.Can.Definitions;

namespace CanKit.Pro.RawCan
{
    /// <summary>
    /// A single, independent, filtered view onto the frames of one <see cref="ICanBusService"/>
    /// (arc42 §5.3 "Multi-Protokoll-Demux"; FR-RAW-010..012).
    /// 对某个 <see cref="ICanBusService"/> 帧流的一路独立、已过滤的只读视图。
    /// </summary>
    /// <remarks>
    /// Each subscription owns its own bounded buffer, so a slow (or never-drained) consumer only
    /// drops its own oldest frames and can never delay delivery to other subscriptions or to the
    /// underlying bus's own <c>FrameObserved</c> event (FR-RAW-011).
    /// <para>
    /// Disposing the subscription deterministically deregisters it from the service (it stops
    /// receiving frames) and completes <see cref="Frames"/>, so any in-flight
    /// <c>await foreach</c> terminates gracefully. Dispose is idempotent (FR-RAW-012).
    /// </para>
    /// <para>
    /// <see cref="Frames"/> is exposed without a cancellation-token parameter to match the arc42
    /// interface shape; pass a token via <c>subscription.Frames.WithCancellation(token)</c> to
    /// stop enumerating early.
    /// </para>
    /// </remarks>
    public interface ISubscription : IDisposable
    {
        /// <summary>
        /// Asynchronously yields the frames accepted by this subscription's filter, in arrival
        /// order, from its own buffer. Completes when the subscription (or its owning service) is
        /// disposed. (按到达顺序从本订阅自有缓冲区异步产出通过过滤的帧；订阅或其所属服务被释放时结束。)
        /// </summary>
        IAsyncEnumerable<CanFrameView> Frames { get; }
    }
}
