using System;



namespace System.Runtime.CompilerServices
{
    internal class IsExternalInit { }
}

namespace Pkuyo.CanKit.Net.Core.Definitions
{
  

    /// <summary>
    /// CAN bus bit timing for initialization (CAN 总线位时序，用于初始化)。
    /// </summary>
    /// <param name="BaudRate">Classic CAN bitrate (经典 CAN 波特率)。</param>
    /// <param name="ArbitrationBitRate">FD arbitration bitrate (FD 仲裁位率)。</param>
    /// <param name="DataBitRate">FD data bitrate (FD 数据位率)。</param>
    public readonly record struct BitTiming(
        uint? BaudRate = 500_000,
        uint? ArbitrationBitRate = null,
        uint? DataBitRate = null);

    /// <summary>
    /// Parameters for sending CAN data (发送 CAN 数据的参数)。
    /// </summary>
    public record CanTransmitData
    {
        /// <summary>
        /// Frame to transmit (待发送的帧)。
        /// </summary>
        public ICanFrame canFrame;
    }

    /// <summary>
    /// Represents a received CAN data event (接收的 CAN 数据事件)。
    /// </summary>
    public record CanReceiveData
    {
        /// <summary>
        /// Received frame (接收到的帧)。
        /// </summary>
        public ICanFrame canFrame;

        /// <summary>
        /// Device-provided timestamp, usually from hardware (设备提供的时间戳，通常来自硬件)。
        /// </summary>
        public UInt64 recvTimestamp;
        /// <summary>
        /// System time corresponding to the record (对应的系统时间)。
        /// </summary>
        public DateTime SystemTimestamp { get;  } = DateTime.Now;
    }

    /// <summary>
    /// CAN bus error counters (CAN 总线错误计数器)。
    /// </summary>
    public record CanErrorCounters
    {

        /// <summary>
        /// Transmit error counter (发送错误计数 TEC)。
        /// </summary>
        public int TransmitErrorCounter { get; init; }
        
        /// <summary>
        /// Receive error counter (接收错误计数 REC)。
        /// </summary>
        public int ReceiveErrorCounter { get; init; }

        public void Deconstruct(out int TransmitErrorCounter, out int ReceiveErrorCounter)
        {
            TransmitErrorCounter = this.TransmitErrorCounter;
            ReceiveErrorCounter = this.ReceiveErrorCounter;
        }
    }
}

