using System.Collections.Generic;
using System.Threading;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{
    /// <summary>
    ///     定义 CAN 收发器在逻辑层面提供的能力。
    /// </summary>
    public interface ITransceiver
    {
        /// <summary>
        ///     通过指定通道发送一个或多个 CAN 帧。
        /// </summary>
        /// <param name="channel">执行发送操作的通道。</param>
        /// <param name="frames">需要发送的帧集合。</param>
        /// <returns>成功写入硬件的帧数量。</returns>
        uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel, params CanTransmitData[] frames);

        /// <summary>
        ///     通过指定通道接收 CAN 帧。
        /// </summary>
        /// <param name="channel">执行接收操作的通道。</param>
        /// <param name="count">期望接收的最大帧数量。</param>
        /// <param name="timeOut">等待数据的超时时间，单位为毫秒，-1 表示无限等待。</param>
        /// <returns>接收到的帧集合。</returns>
        IEnumerable<CanReceiveData> Receive(
            ICanChannel<IChannelRTOptionsConfigurator> channel,
            uint count = 1,
            int timeOut = -1);
    }
}
