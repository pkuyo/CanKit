using System.Collections.Generic;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Virtual;

internal sealed class VirtualTransceiver : ITransceiver
{
    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<CanFrame> frames)
    {
        if (bus is not VirtualBus vBus) return 0;
        int n = 0;
        foreach (var f in frames)
        {
            // validate frame kind vs protocol mode
            if (f.FrameKind is CanFrameType.CanFd && vBus.Options.ProtocolMode != CanProtocolMode.CanFd)
                continue;

            vBus.Hub.Broadcast(vBus, f);
            n++;
        }
        return n;
    }
    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ReadOnlySpan<CanFrame> frames)
    {
        if (bus is not VirtualBus vBus) return 0;
        int n = 0;
        foreach (var f in frames)
        {
            // validate frame kind vs protocol mode
            if (f.FrameKind is CanFrameType.CanFd && vBus.Options.ProtocolMode != CanProtocolMode.CanFd)
                continue;

            vBus.Hub.Broadcast(vBus, f);
            n++;
        }
        return n;
    }

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in CanFrame frame)
    {
        if (bus is not VirtualBus vBus) return 0;
        // validate frame kind vs protocol mode
        if (frame.FrameKind is CanFrameType.CanFd && vBus.Options.ProtocolMode != CanProtocolMode.CanFd)
            return 0;

        vBus.Hub.Broadcast(vBus, frame);

        return 1;
    }

    [Obsolete("We use rxQueue in CanBus instead of VirtualTransceiver.Receive")]
    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
    {
        throw new InvalidOperationException();
    }
}
