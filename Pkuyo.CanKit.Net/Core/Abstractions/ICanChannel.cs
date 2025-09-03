using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{
    
    public interface ICanChannel : IDisposable
    {
        void Start();

        void Reset();
        
        void Stop();

        void CleanBuffer();

        uint Transmit(params CanTransmitData[] frames);

        IEnumerable<CanReceiveData> ReceiveAll(CanFrameType filterType);

        IEnumerable<CanReceiveData> Receive(CanFrameType filterType, uint count = 1, int timeOut = -1);

        uint CanReceiveCount(CanFrameType filterType);

        IChannelRTOptionsConfigurator Options { get; }
        

    }

    public interface ICanChannel<out TConfigurator> : ICanChannel
        where TConfigurator : IChannelRTOptionsConfigurator
    {
        TConfigurator Options { get; }
    }
}
