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
    public IsoTpEndpointRegistration Endpoint
        => new("pcan", PcanEndpoint.Open, PcanEndpoint.Prepare)
    {
        Alias = ["pcanbasic", "peak"],
    };
}
