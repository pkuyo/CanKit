using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{
    public interface ITransceiver
    {
        uint Transmit(ICanChannel<IChannelRTOptionsConfigurator<IChannelOptions>> channel ,params CanTransmitData[] frames);
        
        IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator<IChannelOptions>> channel, uint count = 1, int timeOut = -1);
    }
}