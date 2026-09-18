using System.ComponentModel;
using System.Threading;
using CanKit.Core.Registry;
using CanKit.Transport.IsoTp.Registry;

namespace CanKit.Transport.IsoTp;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class CanKitRegistration
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0) return;
        CanRegistryBootstrap.AddExtension("IsoTp", static () => new RegisterIsoTpEntry());
    }
}
