using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.SocketCAN;

public sealed class SocketCanClassicTransceiver : ITransceiver
{
    public uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator> channel, uint count = 1, int _ = -1)
    {
        throw new NotImplementedException();
    }
}

public sealed class SocketCanFdTransceiver : ITransceiver
{
    public uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator> channel, uint count = 1, int _ = -1)
    {
        throw new NotImplementedException();
    }
}

