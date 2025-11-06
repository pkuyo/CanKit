using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core.Registry;

namespace CanKit.Adapter.SocketCAN.Registers;

[CanRegistryEntry(CanRegistryEntryKind.Adapter, "SocketCAN")]
internal sealed class SocketCanCoreRegister : ICanRegisterFactory, ICanRegisterProviders, IRawRegisterEndpoint
{

    public (string FactoryId, ICanFactory Factory) Factory
        => ("SocketCAN", new SocketCanFactory());

    public IEnumerable<ICanModelProvider> Providers
        => [new SocketCanProvider()];

    public RawEndpointRegistration Endpoint
        => new("socketcan", SocketCanEndpoint.Open, SocketCanEndpoint.Prepare)
        {
            Alias = ["linux", "libsocketcan"],
            Enumerate = SocketCanEndpoint.Enumerate
        };

}

