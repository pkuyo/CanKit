using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CanKit.Core.Definitions;

namespace CanKit.Core.Abstractions
{

    /// <summary>
    /// Represents a CAN bus (表示一个 CAN 总线)，公开最常用的收发与管理能力。
    /// </summary>
    /// <remarks>
    /// EN: A channel abstracts open/close, buffer control and frame TX/RX.
    /// ZH: 通道抽象了打开/关闭、缓冲控制与帧的收发。
    /// </remarks>
    public interface ICanBus : IDisposable
    {
        /// <summary>
        /// Real-time options configurator (运行时通道选项配置器)。
        /// </summary>
        IBusRTOptionsConfigurator Options { get; }

        public BusState BusState { get; }

        /// <summary>
        /// Reset the channel to initial state (复位通道到初始状态)。
        /// </summary>
        void Reset();

        /// <summary>
        /// Clear internal buffers (清空内部缓冲)。
        /// </summary>
        void ClearBuffer();

        /// <summary>
        /// Transmit one or more CAN frames (发送一个或多个 CAN 帧)。
        /// </summary>
        /// <param name="frames">Frames to transmit (待发送帧集合)。</param>
        /// <param name="timeOut">Timeout in ms, -1 for infinite (超时毫秒，-1 表示无限等待)。</param>
        /// <returns>Number of frames accepted by driver (被底层接受的帧数)。</returns>
        uint Transmit(IEnumerable<CanTransmitData> frames, int timeOut = 0);

        /// <summary>
        /// Asynchronously transmit one or more CAN frames (异步发送一个或多个 CAN 帧)
        /// </summary>
        /// <param name="frames">Frames to transmit (待发送帧集合)</param>
        /// <param name="timeOut">Timeout in ms, -1 for infinite (超时毫秒，-1 表示无限等待)</param>
        /// <param name="cancellationToken">Cancellation (取消令牌)</param>
        /// <returns>Number of frames accepted by driver (被底层接受的帧数)</returns>
        Task<uint> TransmitAsync(IEnumerable<CanTransmitData> frames, int timeOut = 0, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Schedule a periodic transmit of a single CAN frame.
        /// 以固定周期定时发送同一帧。
        /// </summary>
        /// <param name="frame">The frame to transmit periodically (需要周期发送的帧)。</param>
        /// <param name="options">Periodic transmit options (周期发送参数)。</param>
        /// <returns>A handle to control the periodic task (用于控制周期任务的句柄)。</returns>
        IPeriodicTx TransmitPeriodic(CanTransmitData frame, PeriodicTxOptions options);

        /// <summary>
        /// Get bus usage ratio (获取总线利用率)。
        /// </summary>
        /// <returns>Usage ratio in percent [0..100] (利用率百分比)。</returns>
        float BusUsage();

        /// <summary>
        /// Get error counters (获取错误计数器，TEC/REC)。
        /// </summary>
        /// <returns>Error counters (错误计数)。</returns>
        CanErrorCounters ErrorCounters();

        /// <summary>
        /// Receive CAN frames (读取 CAN 帧)。
        /// </summary>
        /// <param name="count">Expected frame count (期望读取的帧数)。</param>
        /// <param name="timeOut">Timeout in ms, -1 for infinite (超时毫秒，-1 表示无限等待)。</param>
        /// <returns>Received frames (收到的帧集合)。</returns>
        IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = 0);

        /// <summary>
        /// Asynchronously receive one or more CAN frames (异步读取一个或多个 CAN 帧)
        /// </summary>
        /// <param name="count">Expected frame count (期望读取的帧数)</param>
        /// <param name="timeOut">Timeout in ms, -1 for infinite (超时毫秒，-1 表示无限等待)</param>
        /// <param name="cancellationToken">Cancellation (取消令牌)</param>
        /// <returns>Received frames (收到的帧集合)。当达到期望数量或超时/取消时返回。</returns>
        Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(uint count = 1, int timeOut = 0, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Try read channel error info (获取通道错误信息)。
        /// </summary>
        /// <param name="errorInfo">Error info if present, otherwise null (错误信息或 null)。</param>
        /// <returns>True if error exists (存在错误返回 true)。</returns>
        bool ReadErrorInfo(out ICanErrorInfo? errorInfo);

#if NET8_0_OR_GREATER
        /// <summary>
        /// Stream received frames asynchronously (异步流式获取接收帧)
        /// </summary>
        /// <param name="cancellationToken">Cancellation (取消令牌)</param>
        /// <returns>IAsyncEnumerable of frames</returns>
        IAsyncEnumerable<CanReceiveData> GetFramesAsync(System.Threading.CancellationToken cancellationToken = default);
#endif

        /// <summary>
        /// Raised when a new CAN frame is received (接收到新 CAN 帧时触发)。
        /// </summary>
        event EventHandler<CanReceiveData> FrameReceived;

        /// <summary>
        /// Raised when a channel error occurs (通道发生错误时触发)。
        /// </summary>
        event EventHandler<ICanErrorInfo> ErrorOccurred;
    }


    /// <summary>
    /// Strongly-typed ICanBus with specific RT configurator (带强类型运行时配置器的总线接口)。
    /// </summary>
    /// <typeparam name="TConfigurator">Configurator type (配置器类型)。</typeparam>
    public interface ICanBus<out TConfigurator> : ICanBus
        where TConfigurator : IBusRTOptionsConfigurator
    {
        /// <summary>
        /// Strong-typed RT options (强类型运行时选项)。
        /// </summary>
        new TConfigurator Options { get; }
    }

}
