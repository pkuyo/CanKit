using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Abstractions.API.Can
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

        /// <summary>
        /// Gets the current CAN bus/controller state as reported by the underlying driver
        /// </summary>
        public BusState BusState { get; }

        /// <summary>
        /// Gets the native handle for the underlying bus/channel, primarily for interop
        /// with vendor SDKs or OS APIs. Platform- and adapter-specific; treat it as an
        /// opaque identifier and do not cache it beyond the lifetime of this instance.
        /// （获取底层总线/通道的原生句柄，主要用于与厂商 SDK 或系统 API 互操作。
        /// 该值依赖平台与适配器实现，应视为不透明标识，不应在实例释放后继续缓存使用。）
        /// </summary>
        public BusNativeHandle NativeHandle { get; }

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
        /// <param name="timeOut">
        /// Timeout in milliseconds; use -1 for an infinite wait, and 0 to return immediately without waiting.
        /// Actual behavior is adapter/driver dependent and may not be honored by all hardware.
        /// （超时时间，单位毫秒；-1 表示无限等待，0 表示不等待立即返回。实际行为取决于适配器/驱动，并非所有硬件都会遵循。）
        /// </param>
        /// <returns>Number of frames accepted by driver (被底层接受的帧数)。</returns>
        int Transmit(IEnumerable<CanFrame> frames, int timeOut = 0);

        /// <summary>
        /// Transmit one or more CAN frames (发送一个或多个 CAN 帧)。
        /// </summary>
        /// <param name="frames">Frames to transmit (待发送帧集合)。</param>
        /// <param name="timeOut">
        /// Timeout in milliseconds; use -1 for an infinite wait, and 0 to return immediately without waiting.
        /// Actual behavior is adapter/driver dependent and may not be honored by all hardware.
        /// （超时时间，单位毫秒；-1 表示无限等待，0 表示不等待立即返回。实际行为取决于适配器/驱动，并非所有硬件都会遵循。）
        /// </param>
        /// <returns>Number of frames accepted by driver (被底层接受的帧数)。</returns>
        public int Transmit(ReadOnlySpan<CanFrame> frames, int timeOut = 0);

        /// <summary>
        /// Transmit one or more CAN frames (发送一个或多个 CAN 帧)。
        /// </summary>
        /// <param name="frames">Frames to transmit (待发送帧集合)。</param>
        /// <param name="timeOut">
        /// Timeout in milliseconds; use -1 for an infinite wait, and 0 to return immediately without waiting.
        /// Actual behavior is adapter/driver dependent and may not be honored by all hardware.
        /// （超时时间，单位毫秒；-1 表示无限等待，0 表示不等待立即返回。实际行为取决于适配器/驱动，并非所有硬件都会遵循。）
        /// </param>
        /// <returns>Number of frames accepted by driver (被底层接受的帧数)。</returns>
        public int Transmit(CanFrame[] frames, int timeOut = 0);

        /// <summary>
        /// Transmit one or more CAN frames (发送一个或多个 CAN 帧)。
        /// </summary>
        /// <param name="frames">Frames to transmit (待发送帧集合)。</param>
        /// <param name="timeOut">
        /// Timeout in milliseconds; use -1 for an infinite wait, and 0 to return immediately without waiting.
        /// Actual behavior is adapter/driver dependent and may not be honored by all hardware.
        /// （超时时间，单位毫秒；-1 表示无限等待，0 表示不等待立即返回。实际行为取决于适配器/驱动，并非所有硬件都会遵循。）
        /// </param>
        /// <returns>Number of frames accepted by driver (被底层接受的帧数)。</returns>
        public int Transmit(ArraySegment<CanFrame> frames, int timeOut = 0);

        /// <summary>
        /// Transmit one CAN frame (发送一个 CAN 帧)。
        /// </summary>
        /// <param name="frame">Frames to transmit (待发送帧集合)。</param>
        /// <returns>Number of frames accepted by driver (被底层接受的帧数)。</returns>
        public int Transmit(in CanFrame frame);

        /// <summary>
        /// Asynchronously transmit one or more CAN frames (异步发送一个或多个 CAN 帧)
        /// </summary>
        /// <param name="frames">Frames to transmit (待发送帧集合)</param>
        /// <param name="timeOut">
        /// Timeout in milliseconds; use -1 for an infinite wait, and 0 to return immediately without waiting.
        /// Actual behavior is adapter/driver dependent and may not be honored by all hardware.
        /// （超时时间，单位毫秒；-1 表示无限等待，0 表示不等待立即返回。实际行为取决于适配器/驱动，并非所有硬件都会遵循。）
        /// </param>
        /// <param name="cancellationToken">Cancellation (取消令牌)</param>
        /// <returns>Number of frames accepted by driver (被底层接受的帧数)</returns>
        Task<int> TransmitAsync(IEnumerable<CanFrame> frames, int timeOut = 0, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously transmit one or more CAN frames (异步发送一个或多个 CAN 帧)
        /// </summary>
        /// <param name="frame">Frames to transmit (待发送帧集合)</param>
        /// <param name="cancellationToken">Cancellation (取消令牌)</param>
        /// <returns>Number of frames accepted by driver (被底层接受的帧数)</returns>
        Task<int> TransmitAsync(CanFrame frame, CancellationToken cancellationToken = default);

        /// <summary>
        /// Schedule a periodic transmit of a single CAN frame.
        /// 以固定周期定时发送同一帧。
        /// </summary>
        /// <param name="frame">The frame to transmit periodically (需要周期发送的帧)。</param>
        /// <param name="options">Periodic transmit options (周期发送参数)。</param>
        /// <returns>A handle to control the periodic task (用于控制周期任务的句柄)。</returns>
        IPeriodicTx TransmitPeriodic(CanFrame frame, PeriodicTxOptions options);

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
        /// <param name="timeOut">
        /// Timeout in milliseconds; use -1 for an infinite wait, and 0 to return immediately without waiting.
        /// Actual behavior is adapter/driver dependent and may not be honored by all hardware.
        /// （超时时间，单位毫秒；-1 表示无限等待，0 表示不等待立即返回。实际行为取决于适配器/驱动，并非所有硬件都会遵循。）
        /// </param>
        /// <returns>Received frames (收到的帧集合)。</returns>
        IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0);

        /// <summary>
        /// Asynchronously receive one or more CAN frames (异步读取一个或多个 CAN 帧)
        /// </summary>
        /// <param name="count">Expected frame count (期望读取的帧数)</param>
        /// <param name="timeOut">
        /// Timeout in milliseconds; use -1 for an infinite wait, and 0 to return immediately without waiting.
        /// Actual behavior is adapter/driver dependent and may not be honored by all hardware.
        /// （超时时间，单位毫秒；-1 表示无限等待，0 表示不等待立即返回。实际行为取决于适配器/驱动，并非所有硬件都会遵循。）
        /// </param>
        /// <param name="cancellationToken">Cancellation (取消令牌)</param>
        /// <returns>Received frames (收到的帧集合)。当达到期望数量或超时/取消时返回。</returns>
        Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stream received frames asynchronously (异步流式获取接收帧)
        /// </summary>
        /// <param name="cancellationToken">Cancellation (取消令牌)</param>
        /// <returns>IAsyncEnumerable of frames</returns>
        IAsyncEnumerable<CanReceiveData> GetFramesAsync(CancellationToken cancellationToken = default);


        /// <summary>
        /// Raised when a new CAN frame is received (接收到新 CAN 帧时触发)。
        /// </summary>
        event EventHandler<CanReceiveData> FrameReceived;

        /// <summary>
        /// Raised when a CAN error frame is received（接收到新的 CAN 错误帧的时候触发）。
        /// </summary>
        event EventHandler<ICanErrorInfo> ErrorFrameReceived;

        /// <summary>
        /// Raised when an unexpected error occurs on the bus at background (发生非预期后台异常时触发)。
        /// Implementations should log the error, stop internal loops, cleanup resources,
        /// and ensure pending async operations observe this exception (最终通过该事件传播异常)。
        /// </summary>
        event EventHandler<Exception> BackgroundExceptionOccurred;
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
