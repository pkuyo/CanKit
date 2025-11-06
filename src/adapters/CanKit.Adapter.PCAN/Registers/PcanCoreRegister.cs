using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core.Registry;

namespace CanKit.Adapter.PCAN.Registers;

[CanRegistryEntry(CanRegistryEntryKind.Adapter, "PCAN")]
internal sealed class PcanCoreRegister : ICanRegisterFactory, ICanRegisterProviders, IRawRegisterEndpoint
{

    public (string FactoryId, ICanFactory Factory) Factory
        => ("PCAN", new PcanFactory());

    public IEnumerable<ICanModelProvider> Providers
        => [new PcanProvider()];

    public RawEndpointRegistration Endpoint
        => new("pcan", PcanEndpoint.Open, PcanEndpoint.Prepare)
        {
            Alias = ["pcanbasic", "peak"],
            Enumerate = PcanEndpoint.Enumerate
        };
}

