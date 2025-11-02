using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Virtual.Registers;

[CanRegistryEntry(CanRegistryEntryKind.Adapter, "Virtual")]
internal sealed class VirtualRegister : ICanRegisterFactory, ICanRegisterProviders, ICanRegisterEndpoint
{
    public (string FactoryId, ICanFactory Factory) Factory
        => ("Virtual", new VirtualFactory());

    public IEnumerable<ICanModelProvider> Providers
        => [new VirtualProvider()];

    public EndpointRegistration Endpoint
        => new("virtual", VirtualEndpoint.Open, VirtualEndpoint.Prepare)
        {
            Enumerate = VirtualEndpoint.Enumerate
        };

}

