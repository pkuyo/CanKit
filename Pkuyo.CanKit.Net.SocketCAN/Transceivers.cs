using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.SocketCAN;

public sealed class SocketCanClassicTransceiver : ITransceiver
{
    public uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel, params CanTransmitData[] frames)
    {
        return ((SocketCanChannel)channel).WriteClassic(frames);
    }

    public IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator> channel, uint count = 1, int timeOut = -1)
    {
        return ((SocketCanChannel)channel).ReadClassic(count, timeOut);
    }
}

public sealed class SocketCanFdTransceiver : ITransceiver
{
    public uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel, params CanTransmitData[] frames)
    {
        return ((SocketCanChannel)channel).WriteFd(frames);
    }

    public IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator> channel, uint count = 1, int timeOut = -1)
    {
        return ((SocketCanChannel)channel).ReadFd(count, timeOut);
    }
}

