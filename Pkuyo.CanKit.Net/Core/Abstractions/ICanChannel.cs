using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{

    public interface ICanChannel : IDisposable
    {
        void Open();
        
        void Close();

        void Reset();

        void CleanBuffer();

        uint Transmit(params CanTransmitData[] frames);
        
        IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = -1);

        uint GetReceiveCount();

        IChannelRTOptionsConfigurator Options { get; }

        event EventHandler<CanReceiveData> FrameReceived;
        
        event EventHandler<CanErrorFrame> ErrorOccurred;
    }
    

    public interface ICanChannel<out TConfigurator> : ICanChannel
        where TConfigurator : IChannelRTOptionsConfigurator
    { 
        new TConfigurator Options { get; }
    }
    
}
