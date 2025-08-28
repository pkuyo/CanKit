using System.Collections.Generic;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Abstractions
{
    public interface ITransceiver
    {
        uint Transmit(ICanChannel channel ,params CanTransmitData[] frames);
        
        IEnumerable<CanReceiveData> Receive(ICanChannel channel, uint count = 1, int timeOut = -1);
        
        CanFilterType FilterType { get; }
    }
}