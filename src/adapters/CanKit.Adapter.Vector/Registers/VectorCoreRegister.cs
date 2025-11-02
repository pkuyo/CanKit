using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Vector.Registers;

[CanRegistryEntry(CanRegistryEntryKind.Adapter, "VECTOR")]
internal sealed class VectorCoreRegister : ICanRegisterFactory, ICanRegisterProviders, ICanRegisterEndpoint
{
    public (string FactoryId, ICanFactory Factory) Factory
        => ("VECTOR", new VectorFactory());

    public IEnumerable<ICanModelProvider> Providers
        => [new VectorProvider()];

    public EndpointRegistration Endpoint
        => new("vector", VectorEndpoint.Open, VectorEndpoint.Prepare)
        {
            Alias = ["vxl", "vectorxl"],
            Enumerate = VectorEndpoint.Enumerate
        };
}
