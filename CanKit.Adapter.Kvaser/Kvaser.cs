using System;
using CanKit.Adapter.Kvaser.Definitions;
using CanKit.Core;

namespace CanKit.Adapter.Kvaser;

public static class Kvaser
{
    /// <summary>
    /// Open a Kvaser CANlib channel by channel number.
    /// </summary>
    public static KvaserBus Open(int channel, Action<KvaserBusInitConfigurator>? configure = null)
    {
        return CanBus.Open<KvaserBus, KvaserBusOptions, KvaserBusInitConfigurator>(
            KvaserDeviceType.CANlib,
            cfg =>
            {
                cfg.UseChannelIndex(channel);
                configure?.Invoke(cfg);
            });
    }
}
