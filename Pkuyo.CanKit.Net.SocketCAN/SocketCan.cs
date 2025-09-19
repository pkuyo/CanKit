using System;
using Pkuyo.CanKit.Net;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.SocketCAN.Definitions;

namespace Pkuyo.CanKit.SocketCAN;

public static class SocketCan
{
    public static SocketCanSession Open(Action<SocketCanDeviceInitOptionsConfigurator>? configure = null)
    {
        return (SocketCanSession)Can.Open<SocketCanDevice, SocketCanChannel, SocketCanDeviceOptions, SocketCanDeviceInitOptionsConfigurator>(
            LinuxDeviceType.SocketCAN,
            configure,
            (device, provider) => new SocketCanSession(device, provider));
    }
}

public sealed class SocketCanSession(SocketCanDevice device, ICanModelProvider provider)
    : CanSession<SocketCanDevice, SocketCanChannel>(device, provider)
{
    public SocketCanChannel CreateChannel(int index, Action<SocketCanChannelInitConfigurator>? configure = null)
    {
        return CreateChannel<SocketCanChannelOptions, SocketCanChannelInitConfigurator>(index, configure);
    }

    public SocketCanChannel CreateChannel(string interfaceName, Action<SocketCanChannelInitConfigurator>? configure = null)
    {
        return CreateChannel<SocketCanChannelOptions, SocketCanChannelInitConfigurator>(
            0,
            cfg =>
            {
                cfg.UseInterface(interfaceName);
                configure?.Invoke(cfg);
            });
    }
}

