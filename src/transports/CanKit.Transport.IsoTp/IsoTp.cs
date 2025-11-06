using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Definitions;

namespace CanKit.Transport.IsoTp;

public static class IsoTp
{
    public static IIsoTpChannel Open(string endpoint, IsoTpOptions options, Action<IBusInitOptionsConfigurator>? cfg)
    {
        throw new NotImplementedException();
    }
}
