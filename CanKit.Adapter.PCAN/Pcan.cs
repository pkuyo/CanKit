using CanKit.Adapter.PCAN.Definitions;
using CanKit.Core;

namespace CanKit.Adapter.PCAN;

public static class Pcan
{
    /// <summary>
    /// Open a PCAN channel by channel name, e.g. "PCAN_USBBUS1".
    /// </summary>
    public static PcanBus Open(string channel, Action<PcanBusInitConfigurator>? configure = null)
    {
        return CanBus.Open<PcanBus, PcanBusOptions, PcanBusInitConfigurator>(
            PcanDeviceType.PCANBasic,
            0,
            cfg =>
            {
                cfg.UseChannel(channel);
                configure?.Invoke(cfg);
            });
    }
}

