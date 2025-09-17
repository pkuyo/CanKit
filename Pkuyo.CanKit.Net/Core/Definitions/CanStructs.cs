using System;



namespace System.Runtime.CompilerServices
{
    internal class IsExternalInit { }
}

namespace Pkuyo.CanKit.Net.Core.Definitions
{
  

    /// <summary>
    /// 表示 CAN 总线的位时序配置，用于初始化设备参数。
    /// </summary>
    /// <param name="BaudRate">经典 CAN 的波特率。</param>
    /// <param name="ArbitrationBitRate">仲裁段的比特率。</param>
    /// <param name="DataBitRate">数据段的比特率。</param>
    public readonly record struct BitTiming(
        uint? BaudRate = null,
        uint? ArbitrationBitRate = null,
        uint? DataBitRate = null);

    /// <summary>
    /// 封装一次 CAN 数据发送所需的参数。
    /// </summary>
    public record CanTransmitData
    {
        /// <summary>
        /// 待发送的帧内容。
        /// </summary>
        public ICanFrame canFrame;
    }

    /// <summary>
    /// 表示一次 CAN 数据接收事件。
    /// </summary>
    public record CanReceiveData
    {
        /// <summary>
        /// 接收到的帧内容。
        /// </summary>
        public ICanFrame canFrame;

        /// <summary>
        /// 设备提供的时间戳，通常为硬件计数值。
        /// </summary>
        public UInt64 recvTimestamp;
        /// <summary>
        /// 记录接收时刻对应的系统时间。
        /// </summary>
        public DateTime SystemTimestamp { get;  } = DateTime.Now;
    }
}