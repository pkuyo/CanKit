using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.ZLG.Transceivers
{
    public class ZlgLinTransceiver : IZlgTransceiver
    {
        public uint Transmit(ICanChannel<IChannelRTOptionsConfigurator<IChannelOptions>> channel, params CanTransmitData[] frames)
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator<IChannelOptions>> channel, uint count = 1, int timeOut = -1)
        {
            throw new System.NotImplementedException();
        }

        public ZlgFrameType FrameType => ZlgFrameType.Lin;
    }
}