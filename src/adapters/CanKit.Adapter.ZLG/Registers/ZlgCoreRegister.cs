using System.Collections.Generic;
using System.Linq;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Adapter.ZLG.Providers;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ZLG.Registers;

[CanRegistryEntry(CanRegistryEntryKind.Adapter, "Zlg")]
internal sealed class ZlgCoreRegister : ICanRegisterFactory, ICanRegisterProviders, IRawRegisterEndpoint
{

    /* -----------factory------------ */
    public (string FactoryId, ICanFactory Factory) Factory
        => ("Zlg", new ZlgCanFactory());

    /* -----------providers------------ */
    public IEnumerable<ICanModelProvider> Providers
    {
        get
        {
            var list = new List<ICanModelProvider>();
            var g1 = new USBCANProviderGroup();
            list.AddRange(g1.SupportedDeviceTypes.Select(dt => g1.Create(dt)));
            var g2 = new USBCANFDProviderGroup();
            list.AddRange(g2.SupportedDeviceTypes.Select(dt => g2.Create(dt)));
            var g3 = new CANDTUProviderGroup();
            list.AddRange(g3.SupportedDeviceTypes.Select(dt => g3.Create(dt)));
            var g4 = new PCIECANFDProviderGroup();
            list.AddRange(g4.SupportedDeviceTypes.Select(dt => g4.Create(dt)));
            // standalone provider
            list.Add(new PCIECANFD200UProvider());
            var g5 = new USBCANEProviderGroup();
            list.AddRange(g5.SupportedDeviceTypes.Select(dt => g5.Create(dt)));
            return list;
        }
    }

    /* -----------endpoint------------ */

    public RawEndpointRegistration Endpoint => new("zlg", ZlgEndpoint.Open, ZlgEndpoint.Prepare);

}
