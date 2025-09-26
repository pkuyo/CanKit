using System;
using CanKit.Adapter.SocketCAN.Definitions;
using CanKit.Core;

namespace CanKit.Adapter.SocketCAN;

public static class SocketCan
{
    /// <summary>
    /// Open a SocketCAN channel by interface name (e.g. "can0").
    /// The channel is opened and owns the underlying (null) device lifetime.
    /// </summary>
    public static SocketCanBus Open(string interfaceName, Action<SocketCanBusInitConfigurator>? configure = null)
    {
        return CanBus.Open<SocketCanBus, SocketCanBusOptions, SocketCanBusInitConfigurator>(
            LinuxDeviceType.SocketCAN,
            0,
            cfg =>
            {
                cfg.UseInterface(interfaceName);
                configure?.Invoke(cfg);
            });
    }
}
