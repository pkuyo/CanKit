using System.ComponentModel;
using System.Threading;
using CanKit.Adapter.ZLG.Registers;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ZLG;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class CanKitRegistration
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0) return;
        CanRegistryBootstrap.AddAdapter("Zlg", static () => new ZlgCoreRegister());
    }
}
