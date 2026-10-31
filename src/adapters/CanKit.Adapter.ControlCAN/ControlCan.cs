using System;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Adapter.ControlCAN.Options;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ControlCAN;

public static class ControlCan
{
    public static ControlCanBus Open(Definitions.ControlCanDeviceType deviceType, int deviceIndex, int channelIndex,
        Action<ControlCanBusInitConfigurator>? configure = null)
    {
        if (channelIndex < 0) throw new ArgumentOutOfRangeException(nameof(channelIndex));
        if (deviceIndex < 0) throw new ArgumentOutOfRangeException(nameof(deviceIndex));
        var provider = CanRegistry.Registry.Resolve(deviceType);
        var (devOpt, devCfg) = provider.GetDeviceOptions();
        ((ControlCanDeviceInitOptionsConfigurator)devCfg).DeviceIndex((uint)deviceIndex);
        var (chOpt, chCfg) = provider.GetChannelOptions();
        configure?.Invoke((ControlCanBusInitConfigurator)chCfg);
        chCfg.UseChannelIndex(channelIndex);
        return (ControlCanBus)ControlCanEndpoint.OpenPrepared(
            new PreparedBusContext(provider, devOpt, devCfg, chOpt, chCfg));
    }
}

