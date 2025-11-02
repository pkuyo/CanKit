using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core.Registry;

namespace CanKit.Abstractions.SPI.Registry.Core;

public interface ICanRegisterEndpoint : ICanRegister
{
    public EndpointRegistration Endpoint { get; }
}

