using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core.Registry;

namespace CanKit.Abstractions.SPI.Registry.Core;

public interface IRawRegisterEndpoint : ICanRegister
{
    public RawEndpointRegistration Endpoint { get; }
}

