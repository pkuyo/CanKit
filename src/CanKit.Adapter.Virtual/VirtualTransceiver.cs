using System.Collections.Generic;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Virtual;

internal sealed class VirtualTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        if (channel is not VirtualBus bus) return 0;
        uint n = 0;
        foreach (var f in frames)
        {
            // validate frame kind vs protocol mode
            if (f.CanFrame is CanClassicFrame && channel.Options.ProtocolMode != CanProtocolMode.Can20)
                continue; // skip mismatched
            if (f.CanFrame is CanFdFrame && channel.Options.ProtocolMode != CanProtocolMode.CanFd)
                continue;

            bus.Hub.Broadcast(bus, f.CanFrame);
            n++;
        }
        return n;
    }


    [Obsolete("We use rxQueue in CanBus instead of VirtualTransceiver.Receive")]
    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, uint count = 1, int timeOut = 0)
    {
        throw new InvalidOperationException();
    }
}
