using System.Collections.Generic;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Virtual;

internal sealed class VirtualTransceiver : ITransceiver
{
    public int Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<ICanFrame> frames, int timeOut = 0)
    {
        if (channel is not VirtualBus bus) return 0;
        int n = 0;
        foreach (var f in frames)
        {
            // validate frame kind vs protocol mode
            if (f is CanFdFrame && channel.Options.ProtocolMode != CanProtocolMode.CanFd)
                continue;

            bus.Hub.Broadcast(bus, f);
            n++;
        }
        return n;
    }


    [Obsolete("We use rxQueue in CanBus instead of VirtualTransceiver.Receive")]
    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
    {
        throw new InvalidOperationException();
    }
}
