using System.ComponentModel;
using System.Threading;
using CanKit.Adapter.Vector.Registers;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Vector;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class CanKitRegistration
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0) return;
        CanRegistryBootstrap.AddAdapter("VECTOR", static () => new VectorCoreRegister());
    }
}
