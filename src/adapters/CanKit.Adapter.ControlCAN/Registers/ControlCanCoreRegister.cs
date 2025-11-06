using System.Collections.Generic;
using System.Linq;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Adapter.ControlCAN.Providers;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ControlCAN.Registers;

[CanRegistryEntry(CanRegistryEntryKind.Adapter, "ControlCAN")]
internal sealed class ControlCanCoreRegister : ICanRegisterFactory, ICanRegisterProviders, IRawRegisterEndpoint
{

    public (string FactoryId, ICanFactory Factory) Factory
        => ("ControlCAN", new ControlCanFactory());

    public IEnumerable<ICanModelProvider> Providers
    {
        get
        {
            var list = new List<ICanModelProvider>();
            var g1 = new ControlCanProviderGroup();
            list.AddRange(g1.SupportedDeviceTypes.Select(dt => g1.Create(dt)));
            var g2 = new ControlCanRProviderGroup();
            list.AddRange(g2.SupportedDeviceTypes.Select(dt => g2.Create(dt)));
            return list;
        }
    }

    public RawEndpointRegistration Endpoint =>
        new("controlcan", ControlCanEndpoint.Open, ControlCanEndpoint.Prepare);

}

