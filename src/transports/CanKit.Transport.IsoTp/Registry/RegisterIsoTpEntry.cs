using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Abstractions.SPI.Registry.Transports;

namespace CanKit.Transport.IsoTp.Registry;


[CanRegistryEntry(CanRegistryEntryKind.Transport, "IsoTp")]
public class RegisterIsoTpEntry : ICanRegistryEntry
{
    public void Register(string name, ICanRegister register)
    {
        if (register is not IIsoTpRegister iso) return;
        IsoTpRegistry.Enqueue(iso.Endpoint);

    }
}
