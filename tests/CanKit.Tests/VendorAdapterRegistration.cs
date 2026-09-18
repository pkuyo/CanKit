using System.Runtime.CompilerServices;

namespace CanKit.Tests;

internal static class VendorAdapterRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        global::CanKit.Adapter.ControlCAN.CanKitRegistration.Register();
        global::CanKit.Adapter.Kvaser.CanKitRegistration.Register();
        global::CanKit.Adapter.PCAN.CanKitRegistration.Register();
        global::CanKit.Adapter.SocketCAN.CanKitRegistration.Register();
        global::CanKit.Adapter.Vector.CanKitRegistration.Register();
        global::CanKit.Adapter.Virtual.CanKitRegistration.Register();
        global::CanKit.Adapter.ZLG.CanKitRegistration.Register();
    }
}
