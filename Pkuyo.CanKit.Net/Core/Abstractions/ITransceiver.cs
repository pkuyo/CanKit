using System.Collections.Generic;
using System.Threading;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{
    public interface ITransceiver
    {
        uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel ,params CanTransmitData[] frames);
        
        IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator> channel, uint count = 1, int timeOut = -1);
        
    }
}