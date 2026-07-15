namespace CanKit.Pro.Actor
{
    /// <summary>
    /// Chooses the execution context a <see cref="ProtocolActor"/> runs its single mailbox loop
    /// on (arc42 §8.3/ADR-6; FR-RAW-024). (选择 <see cref="ProtocolActor"/> 运行其单一邮箱循环所使用的
    /// 执行上下文。)
    /// </summary>
    public enum ActorExecutionMode
    {
        /// <summary>
        /// Default: a genuine dedicated <see cref="System.Threading.Thread"/>, reserved for the
        /// lifetime of the actor. Every posted work item and timer callback demonstrably runs on
        /// that exact thread, never hops to another one across calls. Best for protocol instances
        /// with real-time-ish timing needs (STmin, N_As/N_Bs) where predictable scheduling
        /// matters more than the cost of an extra OS thread. (默认：真正独占的专用线程，在整个 Actor
        /// 生命周期内保留；每个已投递的工作项与定时器回调均可验证地运行在这同一线程上，跨调用不会切换。)
        /// </summary>
        DedicatedThread,

        /// <summary>
        /// The mailbox loop runs as a normal <see cref="System.Threading.Tasks.Task"/> on the
        /// .NET thread pool. Still strictly single-writer (never two work items execute
        /// concurrently), but successive items are not guaranteed to run on the same OS thread.
        /// Cheaper than <see cref="DedicatedThread"/> when many short-lived protocol instances
        /// exist simultaneously. (邮箱循环作为普通 Task 运行在 .NET 线程池上：仍严格单写者（工作项永不并发
        /// 执行），但先后两个工作项不保证运行在同一操作系统线程上；适合同时存在大量短生命周期协议实例的场景。)
        /// </summary>
        ThreadPool,

        /// <summary>
        /// Every posted work item and timer callback is marshaled onto a caller-supplied
        /// <see cref="System.Threading.SynchronizationContext"/> (e.g. a UI dispatcher) via
        /// <see cref="System.Threading.SynchronizationContext.Post"/>, so protocol callbacks can
        /// safely touch UI-bound state without the caller manually marshaling. Requires passing a
        /// non-null context to <see cref="ProtocolActor(ActorExecutionMode, System.Threading.SynchronizationContext?)"/>.
        /// (每个已投递的工作项与定时器回调都会通过 <see cref="System.Threading.SynchronizationContext.Post"/>
        /// 转发到调用方提供的同步上下文（例如 UI 调度器），使协议回调可以安全地操作 UI 绑定状态而无需调用方手动转发；
        /// 要求向构造函数传入非 null 的上下文。)
        /// </summary>
        SynchronizationContext,
    }
}
