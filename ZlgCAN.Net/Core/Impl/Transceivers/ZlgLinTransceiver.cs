using System.Collections.Generic;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Definitions;
using ZlgCAN.Net.Core.Transceivers;

namespace ZlgCAN.Net.Core.Impl.Transceivers
{
    public class ZlgLinTransceiver : IZlgTransceiver
    {
        public uint Transmit(ICanChannel channel, params CanTransmitData[] frames)
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<CanReceiveData> Receive(ICanChannel channel, uint count = 1, int timeOut = -1)
        {
            throw new System.NotImplementedException();
        }

        public ZlgFrameType FrameType => ZlgFrameType.Lin;
    }
}