using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;

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
        /// Raised when a caller-supplied subscription filter predicate throws during dispatch
        /// (FR-RAW-023-style fault channel). The failing frame is isolated to that subscription
        /// (delivery to the other subscriptions continues), and the exception is surfaced here
        /// instead of being silently swallowed. Invoked synchronously on the bus's dispatch
        /// thread, so handlers must return quickly and must not call back into the service.
        /// (当订阅方提供的过滤谓词在分发过程中抛出异常时触发。出错帧仅隔离于该订阅（其余订阅
        /// 照常投递），异常经此通道上抛而非静默吞没。在总线分发线程上同步调用，处理程序必须
        /// 快速返回且不得回调本服务。)
        /// </summary>
        event EventHandler<Exception>? BackgroundExceptionOccurred;

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

        /// <summary>
        /// Diagnostic: finds every pair of currently registered, still-undisposed
        /// <see cref="CanIdFilter"/>-based subscriptions whose ID spaces overlap (FR-RAW-041,
        /// "Should") -- helps catch misconfiguration when multiple protocol instances were meant
        /// to have disjoint ID ranges but don't. Subscriptions registered via the generic
        /// <see cref="Subscribe(Func{CanFrameView,bool}, int?)"/> predicate overload are opaque
        /// and are not analyzable, so they are skipped. (诊断：查找当前所有已注册、尚未释放、基于
        /// <see cref="CanIdFilter"/> 的订阅中，ID 空间存在重叠的每一对——用于发现多个协议实例本应互不重叠、
        /// 但实际配置错误导致重叠的情形。通过泛型谓词重载注册的订阅是不透明的，无法分析，将被跳过。)
        /// </summary>
        IReadOnlyList<(ISubscription First, ISubscription Second)> FindOverlappingFilterSubscriptions();

        /// <summary>
        /// Sends <paramref name="frame"/> and asynchronously confirms it was actually sent, using
        /// a uniform abstraction regardless of whether the underlying bus has hardware TX echo
        /// enabled (arc42 §6.3, ADR-7; FR-RAW-030). When the bus both declares
        /// <see cref="CanFeature.Echo"/> and has <c>WorkMode == ChannelWorkMode.Echo</c> configured,
        /// confirmation comes from an actually-matched echo frame (FR-RAW-031, including correct
        /// FIFO matching of multiple concurrent byte-identical sends — no cross-matching or crash);
        /// otherwise it is a documented approximation based on driver acceptance
        /// (<see cref="TxConfirmation.IsApproximated"/>, FR-RAW-032). Never hangs: timeout,
        /// bus-off, and outright rejection all resolve the returned task within bounded time
        /// (FR-RAW-033) — see <see cref="TxConfirmation"/> for exactly how.
        /// (发送 <paramref name="frame"/> 并异步确认其确已被发送，无论底层总线是否启用了硬件发送回显均采用统一的抽象。
        /// 当总线同时声明 <see cref="CanFeature.Echo"/> 且配置了 <c>WorkMode == ChannelWorkMode.Echo</c> 时，
        /// 确认来自实际匹配到的回显帧（含对多路并发、字节内容相同的发送的正确 FIFO 匹配，不会互相匹配错乱或崩溃）；
        /// 否则为基于驱动接受的、有文档说明的近似确认。永不悬挂：超时、总线关闭（BusOff）以及被驱动直接拒绝，
        /// 均会在有限时间内使返回的任务得到结果。)
        /// </summary>
        /// <param name="frame">
        /// The frame to send. As with <see cref="ICanBus.Transmit(in CanFrame)"/>, the caller
        /// remains the owner (TX-lease) and is responsible for disposing it after this call
        /// returns/completes — <see cref="ICanBusService"/> never disposes it.
        /// (要发送的帧。与 <see cref="ICanBus.Transmit(in CanFrame)"/> 一致，调用方始终是所有者（TX 租约），
        /// 本调用返回/完成后需自行释放该帧——<see cref="ICanBusService"/> 不会释放它。)
        /// </param>
        /// <param name="timeout">
        /// Maximum time to wait for an echo before failing with
        /// <see cref="TxConfirmFailureReason.Timeout"/> (FR-RAW-034); null uses
        /// <see cref="CanBusService.DefaultConfirmTimeout"/>. Ignored on the approximated path
        /// (driver acceptance is synchronous/immediate). Must be positive.
        /// (等待回显的最长时间，超时后以 <see cref="TxConfirmFailureReason.Timeout"/> 失败；null 表示使用
        /// <see cref="CanBusService.DefaultConfirmTimeout"/>。近似确认路径下忽略此参数（驱动接受是同步/即时的）。
        /// 必须为正值。)
        /// </param>
        /// <param name="cancellationToken">
        /// Caller-supplied cancellation; cancels the returned task per standard .NET convention,
        /// distinct from the domain-level <see cref="TxConfirmFailureReason.Timeout"/> outcome.
        /// (调用方提供的取消令牌；按 .NET 标准约定取消返回的任务，与领域级别的
        /// <see cref="TxConfirmFailureReason.Timeout"/> 结果是两回事。)
        /// </param>
        Task<TxConfirmation> SendConfirmed(CanFrame frame, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    }
}
