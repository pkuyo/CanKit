using System;
using System.Threading.Tasks;

namespace CanKit.Pro.Actor
{
    /// <summary>
    /// A protocol instance's documented threading model (arc42 §8.3, ADR-6; FR-RAW-020..024):
    /// exactly one mailbox, processed by exactly one logical loop, so a protocol instance's
    /// internal state (channel registers, timer queues, state machines) is never mutated from
    /// more than one place at a time — no locks needed by the protocol code itself
    /// (FR-RAW-020/021). Scheduling is event-driven, never a busy loop (FR-RAW-022); background
    /// exceptions are surfaced via <see cref="BackgroundExceptionOccurred"/> instead of being
    /// thrown on some unrelated caller thread or lost as an unobserved task exception
    /// (FR-RAW-023). (协议实例的既定线程模型：恰好一个邮箱，由恰好一个逻辑循环处理，因此协议实例的内部状态
    /// （通道寄存器、定时器队列、状态机）永远不会被多处同时修改——协议代码自身无需加锁；调度采用事件驱动，绝非
    /// 忙等待循环；后台异常通过 <see cref="BackgroundExceptionOccurred"/> 对外暴露，而不是抛到某个无关的
    /// 调用方线程上或作为未观测的任务异常丢失。)
    /// </summary>
    public interface IProtocolActor : IDisposable
    {
        /// <summary>
        /// Enqueues <paramref name="work"/> to run on the actor's mailbox loop and returns
        /// immediately ("tell" / fire-and-forget). If <paramref name="work"/> throws, the
        /// exception is caught by the loop and surfaced via
        /// <see cref="BackgroundExceptionOccurred"/> — there is no other way for a fire-and-forget
        /// caller to observe it (FR-RAW-023). (将 <paramref name="work"/> 加入 Actor 的邮箱循环并立即
        /// 返回（“告知”/即发即弃）；若 <paramref name="work"/> 抛出异常，循环会捕获并通过
        /// <see cref="BackgroundExceptionOccurred"/> 对外暴露——对于即发即弃的调用方而言，这是观测异常的唯一途径。)
        /// </summary>
        void Post(Action work);

        /// <summary>
        /// Enqueues <paramref name="work"/> to run on the actor's mailbox loop and returns a task
        /// that completes once it has run ("ask"). Unlike <see cref="Post"/>, an exception from
        /// <paramref name="work"/> is surfaced through the returned task's fault, not through
        /// <see cref="BackgroundExceptionOccurred"/> — the caller is already positioned to observe
        /// it by awaiting. (将 <paramref name="work"/> 加入 Actor 的邮箱循环，并返回一个在其执行完毕后完成
        /// 的任务（“请求”）；与 <see cref="Post"/> 不同，<paramref name="work"/> 抛出的异常通过返回任务的
        /// 故障状态呈现，而不经过 <see cref="BackgroundExceptionOccurred"/>——调用方通过 await 即可观测到。)
        /// </summary>
        Task PostAsync(Action work);

        /// <summary>
        /// Same as <see cref="PostAsync(Action)"/> but returns <paramref name="work"/>'s result.
        /// (与 <see cref="PostAsync(Action)"/> 相同，但返回 <paramref name="work"/> 的结果。)
        /// </summary>
        Task<T> PostAsync<T>(Func<T> work);

        /// <summary>
        /// Schedules <paramref name="callback"/> to run on the actor's mailbox loop once
        /// <paramref name="delay"/> has elapsed, using event-driven waiting rather than polling
        /// (FR-RAW-022) — suitable for STmin waits, timeout checks, and similar periodic/timed
        /// protocol tasks. Disposing the returned handle cancels the callback on a best-effort
        /// basis: it will not fire if cancellation is observed before it becomes due, but a
        /// callback already in flight on the loop may still complete. (在经过 <paramref name="delay"/>
        /// 之后于 Actor 的邮箱循环上运行 <paramref name="callback"/>，采用事件驱动等待而非轮询，适用于
        /// STmin 等待、超时检查等周期性/限时协议任务；释放返回的句柄会尽力取消该回调——若在到期前观测到取消则不会
        /// 触发，但已经在循环中执行的回调可能仍会完成。)
        /// </summary>
        IDisposable Schedule(TimeSpan delay, Action callback);

        /// <summary>
        /// Raised whenever a posted work item (via <see cref="Post"/>) or a scheduled callback
        /// (via <see cref="Schedule"/>) throws — the actor's single, defined channel for
        /// background exceptions (FR-RAW-023). The mailbox loop keeps running afterward; one
        /// failing item never stops the actor. Never raised for <see cref="PostAsync(Action)"/>/
        /// <see cref="PostAsync{T}(Func{T})"/> failures, which surface through their own returned
        /// task instead. (每当通过 <see cref="Post"/> 投递的工作项或通过 <see cref="Schedule"/> 调度的回调
        /// 抛出异常时触发——这是 Actor 用于后台异常的唯一既定通道；邮箱循环之后继续运行，单个失败项目不会使
        /// Actor 停止。<see cref="PostAsync(Action)"/>/<see cref="PostAsync{T}(Func{T})"/> 的失败不会触发此
        /// 事件，而是通过各自返回的任务呈现。)
        /// </summary>
        event EventHandler<Exception> BackgroundExceptionOccurred;
    }
}
