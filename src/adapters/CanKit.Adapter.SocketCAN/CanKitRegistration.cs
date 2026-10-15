using System.ComponentModel;
using System.Threading;
using CanKit.Adapter.SocketCAN.Registers;
using CanKit.Core.Registry;

namespace CanKit.Adapter.SocketCAN;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class CanKitRegistration
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0) return;
        CanRegistryBootstrap.AddAdapter("SocketCAN", static () => new SocketCanCoreRegister());
    }
}
