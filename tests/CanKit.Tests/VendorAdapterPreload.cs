using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CanKit.Tests;

internal static class VendorAdapterPreload
{
    [ModuleInitializer]
    internal static void Load()
    {
        TryLoad("CanKit.Adapter.Kvaser");
        TryLoad("CanKit.Adapter.PCAN");
        TryLoad("CanKit.Adapter.Vector");
        TryLoad("CanKit.Adapter.ControlCAN");
        TryLoad("CanKit.Adapter.ZLG");
    }

    private static void TryLoad(string name)
    {
        try
        {
#if NET5_0_OR_GREATER
            System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(name));
#else
            Assembly.Load(new AssemblyName(name));
#endif
        }
        catch
        {
            /* ignore missing adapters in partial test hosts */
        }
    }
}
