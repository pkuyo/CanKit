using System.Collections.Generic;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Candlelight.Registers;

[CanRegistryEntry(CanRegistryEntryKind.Adapter, "Candlelight")]
internal sealed class CandlelightCoreRegister : ICanRegisterFactory, ICanRegisterProviders, IRawRegisterEndpoint
{
    public (string FactoryId, ICanFactory Factory) Factory => ("Candlelight", new CandlelightFactory());
    public IEnumerable<ICanModelProvider> Providers => [new CandlelightProvider()];
    public RawEndpointRegistration Endpoint => new("candlelight", CandlelightEndpoint.Open, CandlelightEndpoint.Prepare)
    {
        Enumerate = CandlelightEndpoint.Enumerate
    };
}
