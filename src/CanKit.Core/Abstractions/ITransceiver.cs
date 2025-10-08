using System.Collections.Generic;
using CanKit.Core.Definitions;

namespace CanKit.Core.Abstractions
{
    /// <summary>
    /// Defines the logical capabilities provided by a CAN transceiver. （定义 CAN 收发器在逻辑层面提供的能力）
    /// </summary>
    /// <remarks>
    /// NOTE: The effectiveness of <c>timeOut</c> depends on the underlying adapter/driver and is not guaranteed
    /// on all devices; some implementations may ignore it or approximate it with retries/sleep loops.
    /// （注意：<c>timeOut</c> 的有效性依赖具体适配器/驱动，并非所有设备都保证支持；部分实现可能忽略该值，或通过重试/短暂休眠近似实现。）
    /// </remarks>
    public interface ITransceiver
    {
        /// <summary>
        /// Sends one or more CAN frames via the specified channel. （通过指定通道发送一个或多个 CAN 帧）
        /// </summary>
        /// <param name="channel">The channel on which to transmit. （执行发送操作的通道）</param>
        /// <param name="frames">The frames to transmit. （需要发送的帧集合）</param>
        /// <param name="timeOut">
        /// Timeout in milliseconds; use -1 for an infinite wait, and 0 to return immediately without waiting.
        /// Actual behavior is adapter/driver dependent and may not be honored by all hardware.
        /// （超时时间，单位毫秒；-1 表示无限等待，0 表示不等待立即返回。实际行为取决于适配器/驱动，并非所有硬件都会遵循。）
        /// </param>
        /// <returns>
        /// The number of frames successfully written to the hardware (may be less than requested if a timeout occurs).
        /// （成功写入硬件的帧数量；若发生超时，可能小于请求数量。）
        /// </returns>
        uint Transmit(
            ICanBus<IBusRTOptionsConfigurator> channel,
            IEnumerable<CanTransmitData> frames,
            int timeOut = 0);

        /// <summary>
        /// Receives CAN frames from the specified channel. （通过指定通道接收 CAN 帧）
        /// </summary>
        /// <param name="bus">The channel on which to receive. （执行接收操作的通道）</param>
        /// <param name="count">The maximum number of frames to receive. （期望接收的最大帧数量）</param>
        /// <param name="timeOut">
        /// Timeout in milliseconds; use -1 for an infinite wait, and 0 to return immediately without waiting.
        /// Actual behavior is adapter/driver dependent and may not be honored by all hardware.
        /// （超时时间，单位毫秒；-1 表示无限等待，0 表示不等待立即返回。实际行为取决于适配器/驱动，并非所有硬件都会遵循。）
        /// </param>
        /// <returns>
        /// The collection of frames received within the time window (possibly empty).
        /// （在超时窗口内接收到的帧集合，可能为空。）
        /// </returns>
        IEnumerable<CanReceiveData> Receive(
            ICanBus<IBusRTOptionsConfigurator> bus,
            uint count = 1,
            int timeOut = 0);
    }
}
