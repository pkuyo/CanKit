using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Kvaser.Registers;

[CanRegistryEntry(CanRegistryEntryKind.Adapter, "KVASER")]
internal sealed class KvaserCoreRegister : ICanRegisterFactory, ICanRegisterProviders, IRawRegisterEndpoint
{
    public (string FactoryId, ICanFactory Factory) Factory
        => ("KVASER", new KvaserFactory());

    public IEnumerable<ICanModelProvider> Providers
        => [new KvaserProvider()];

    public RawEndpointRegistration Endpoint
        => new("kvaser", KvaserEndpoint.Open, KvaserEndpoint.Prepare)
        {
            Alias = ["canlib"],
            Enumerate = KvaserEndpoint.Enumerate,
        };
}

