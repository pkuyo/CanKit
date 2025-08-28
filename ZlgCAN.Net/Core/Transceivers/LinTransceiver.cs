using System.Collections.Generic;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Transceivers
{
    public class LinTransceiver : ITransceiver
    {
        public uint Transmit(ICanChannel channel, params CanTransmitData[] frames)
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<CanReceiveData> Receive(ICanChannel channel, uint count = 1, int timeOut = -1)
        {
            throw new System.NotImplementedException();
        }

        public CanFilterType FilterType => CanFilterType.Lin;
    }
}