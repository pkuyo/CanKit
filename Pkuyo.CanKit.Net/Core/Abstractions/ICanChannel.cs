using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{

    /// <summary>
    ///     表示 CAN 通道在运行时暴露的最小功能集合。
    /// </summary>
    /// <remarks>
    ///     通道负责协调底层设备的打开、关闭以及报文的收发等生命周期操作。
    /// </remarks>
    public interface ICanChannel : IDisposable
    {
        /// <summary>
        ///     打开当前通道并使其进入可通信的工作状态。
        /// </summary>
        void Open();

        /// <summary>
        ///     关闭当前通道并释放与之相关的临时资源。
        /// </summary>
        void Close();

        /// <summary>
        ///     将通道恢复到初始状态，通常用于错误恢复或重新初始化。
        /// </summary>
        void Reset();

        /// <summary>
        ///     清空通道内部缓存的数据，例如接收缓存和发送队列。
        /// </summary>
        void CleanBuffer();

        /// <summary>
        ///     向总线上发送一个或多个 CAN 帧。
        /// </summary>
        /// <param name="frames">需要发送的帧集合。</param>
        /// <returns>成功写入底层硬件缓冲区的帧数量。</returns>
        uint Transmit(params CanTransmitData[] frames);

        /// <summary>
        ///     从通道中读取 CAN 帧。
        /// </summary>
        /// <param name="count">希望读取的最大帧数。</param>
        /// <param name="timeOut">等待数据的超时时间，单位为毫秒，-1 表示无限等待。</param>
        /// <returns>读取到的 CAN 帧序列。</returns>
        IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = -1);

        /// <summary>
        ///     获取当前接收缓冲区中可读取的帧数量。
        /// </summary>
        /// <returns>接收缓冲区中帧的数量。</returns>
        uint GetReceiveCount();

        /// <summary>
        ///     获取用于访问运行时参数的配置器。
        /// </summary>
        IChannelRTOptionsConfigurator Options { get; }

        /// <summary>
        ///     当收到新的 CAN 帧时触发。
        /// </summary>
        event EventHandler<CanReceiveData> FrameReceived;

        /// <summary>
        ///     当通道运行时出现错误帧时触发。
        /// </summary>
        event EventHandler<ICanErrorInfo> ErrorOccurred;
    }


    /// <summary>
    ///     带有强类型配置器的 CAN 通道接口。
    /// </summary>
    /// <typeparam name="TConfigurator">通道运行时配置器的具体类型。</typeparam>
    public interface ICanChannel<out TConfigurator> : ICanChannel
        where TConfigurator : IChannelRTOptionsConfigurator
    {
        /// <summary>
        ///     获取强类型的运行时配置访问器。
        /// </summary>
        new TConfigurator Options { get; }
    }

}
