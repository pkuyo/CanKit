using Pkuyo.CanKit.SocketCAN.Definitions;
using Pkuyo.CanKit.Net;
using System;

namespace Pkuyo.CanKit.Net.SocketCAN;

public static class SocketCan
{
    /// <summary>
    /// Open a SocketCAN channel by interface name (e.g. "can0").
    /// The channel is opened and owns the underlying (null) device lifetime.
    /// </summary>
    public static SocketCanChannel Open(string interfaceName, Action<SocketCanChannelInitConfigurator>? configure = null)
    {
        return CanBus.Open<SocketCanChannel, SocketCanChannelOptions, SocketCanChannelInitConfigurator>(
            LinuxDeviceType.SocketCAN,
            0,
            cfg =>
            {
                cfg.UseInterface(interfaceName);
                configure?.Invoke(cfg);
            });
    }

    /// <summary>
    /// Open a SocketCAN channel by numeric index (maps to can{index}).
    /// </summary>
    public static SocketCanChannel Open(int channelIndex = 0, Action<SocketCanChannelInitConfigurator>? configure = null)
    {
        return CanBus.Open<SocketCanChannel, SocketCanChannelOptions, SocketCanChannelInitConfigurator>(
            LinuxDeviceType.SocketCAN,
            channelIndex,
            configure);
    }
}
