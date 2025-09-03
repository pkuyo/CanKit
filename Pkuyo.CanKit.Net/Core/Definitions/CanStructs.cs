using System;



namespace System.Runtime.CompilerServices
{
    internal class IsExternalInit { }
}

namespace Pkuyo.CanKit.Net.Core.Definitions
{
  

    public readonly record struct BitTiming(
        uint? BaudRate = null,
        uint? ArbitrationBitRate = null,
        uint? DataBitRate = null);

    public record CanTransmitData
    {
        public ICanFrame canFrame;
    }

    public record CanReceiveData
    {
        public ICanFrame canFrame;
        public UInt64 timestamp;

        public DateTime Timestamp => DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp).DateTime;
    }
}