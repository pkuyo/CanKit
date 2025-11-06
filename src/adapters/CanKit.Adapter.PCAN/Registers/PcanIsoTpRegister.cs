using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Abstractions.SPI.Registry.Transports;
using CanKit.Adapter.PCAN.Transport;

namespace CanKit.Adapter.PCAN.Registers;

public class PcanIsoTpRegister : IIsoTpRegister
{
    public IIsoTpChannel Open(CanEndpoint endpoint, IsoTpOptions options, Action<IBusInitOptionsConfigurator>? cfg = null)
    {
        var ctx = PcanEndpoint.Prepare(endpoint, cfg);
        var handle = PcanProvider.ParseHandle(ctx.BusOptions.ChannelName!);
        var (channel, lease) = PcanIsoTpBusMultiplexer.Acquire(handle,
            () => new PcanIsoTpScheduler(ctx.BusOptions),
            (bus) => new PcanIsoTpChannel((PcanIsoTpScheduler)bus, options));
        if(channel is IOwnership ownership)
            ownership.AttachOwner(lease);
        return channel;
    }
}
