using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;

namespace CanKit.Pro.RawCan
{
    /// <summary>
    /// One demultiplexing service instance per <see cref="ICanBus"/>: it turns the single RX
    /// stream exposed by <see cref="ICanBus.FrameObserved"/> into N independent, filtered
    /// read-only <see cref="ISubscription"/>s, so multiple protocol instances (ISO-TP, J1939,
    /// CANopen, …) can each see their own view of the bus without competing over
    /// <see cref="ICanBus.ReceiveAsync"/> (arc42 §5.3, ADR-5; FR-RAW-010..013).
    /// 每个 <see cref="ICanBus"/> 对应一个解复用服务实例：将 <see cref="ICanBus.FrameObserved"/>
    /// 暴露的单一接收流分发为 N 路独立、已过滤的只读订阅，使多个协议实例互不争抢
    /// <see cref="ICanBus.ReceiveAsync"/>。
    /// </summary>
    /// <remarks>
    /// The service is built purely on top of the public <see cref="ICanBus.FrameObserved"/>
    /// surface (a read-only <see cref="CanFrameView"/> per frame, with no disposal/ownership
    /// concerns), so it works identically for every adapter with no per-adapter changes.
    /// Disposing the service unwinds all outstanding subscriptions and detaches its handler from
    /// the underlying bus (FR-RAW-012). Dispose is idempotent.
    /// </remarks>
    public interface ICanBusService : IDisposable
    {
        /// <summary>
        /// The underlying bus this service demultiplexes. (本服务解复用的底层总线。)
        /// </summary>
        ICanBus Bus { get; }

        /// <summary>
        /// Number of currently registered (not yet disposed) subscriptions. Primarily for
        /// diagnostics/tests: after disposing every subscription it returns to zero, proving no
        /// registry entries leak (FR-RAW-012). (当前已注册且未释放的订阅数量，主要用于诊断/测试。)
        /// </summary>
        int SubscriptionCount { get; }

        /// <summary>
        /// Registers a subscription that receives every frame for which
        /// <paramref name="predicate"/> returns true; a null predicate accepts all frames
        /// (FR-RAW-010). (注册一路订阅，接收所有使 <paramref name="predicate"/> 返回 true 的帧；
        /// 传入 null 表示接收全部帧。)
        /// </summary>
        /// <param name="predicate">Per-frame filter, or null to accept all frames. (逐帧过滤谓词，或 null 表示接收全部。)</param>
        /// <param name="bufferCapacity">
        /// Bounded buffer capacity for this subscription; null uses
        /// <see cref="CanBusService.DefaultBufferCapacity"/>. When the buffer is full the oldest
        /// buffered frame is dropped so dispatch never blocks (FR-RAW-011).
        /// (本订阅的有界缓冲容量；null 使用默认值。缓冲满时丢弃最旧的帧，分发路径永不阻塞。)
        /// </param>
        ISubscription Subscribe(Func<CanFrameView, bool>? predicate = null, int? bufferCapacity = null);

        /// <summary>
        /// Registers a subscription using the allocation-free ID-range/mask fast path
        /// (FR-RAW-010/013). (使用免分配的 ID 范围/掩码快速路径注册一路订阅。)
        /// </summary>
        /// <param name="filter">ID-range or acceptance-code/mask filter. (ID 范围或验收码/掩码过滤器。)</param>
        /// <param name="bufferCapacity">
        /// Bounded buffer capacity for this subscription; null uses
        /// <see cref="CanBusService.DefaultBufferCapacity"/>. (本订阅的有界缓冲容量；null 使用默认值。)
        /// </param>
        ISubscription Subscribe(CanIdFilter filter, int? bufferCapacity = null);
    }
}
