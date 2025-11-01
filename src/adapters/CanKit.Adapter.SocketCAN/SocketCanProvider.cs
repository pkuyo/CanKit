using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.SocketCAN.Definitions;
using CanKit.Adapter.SocketCAN.Native;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Registry;

namespace CanKit.Adapter.SocketCAN;

public sealed class SocketCanProvider : ICanModelProvider, ICanCapabilityProvider
{
    public DeviceType DeviceType => LinuxDeviceType.SocketCAN;

    public CanFeature StaticFeatures => CanFeature.CanClassic | CanFeature.CanFd | CanFeature.MaskFilter |
                                        CanFeature.ErrorFrame | CanFeature.ErrorCounters | CanFeature.CyclicTx;

    public ICanFactory Factory => CanRegistry.Registry.Factory("SocketCAN");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var options = new NullDeviceOptions(this);
        var cfg = new NullDeviceInitOptionsConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
    {
        var options = new SocketCanBusOptions(this)
        {
            BitTiming = CanBusTiming.ClassicDefault(),
            ProtocolMode = CanProtocolMode.Can20,
            WorkMode = ChannelWorkMode.Normal,
            ChannelName = $"can0"
        };
        var cfg = new SocketCanBusInitConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    public Capability QueryCapabilities(IBusOptions busOptions)
    {
        if (LibSocketCan.can_get_ctrlmode(busOptions.ChannelName, out var ctrlMode) != Libc.OK)
        {
            var re = Libc.Errno();
            if (re == Libc.EOPNOTSUPP)
            {
                CanKitLogger.LogInformation($"SocketCanBus: {busOptions.ChannelName} not support ctrlmode. Ignored socket can config.");
                return new Capability(StaticFeatures);
            }
            Libc.ThrowErrno("can_get_ctrlmode", $"Failed to get ctrlmode for '{busOptions.ChannelName}'", re);
        }

        var mask = ctrlMode.mask;
        var features = CanFeature.CanClassic | CanFeature.RangeFilter | CanFeature.ErrorCounters;
        if ((mask & LibSocketCan.CAN_CTRLMODE_LOOPBACK) != 0)
            features |= CanFeature.Echo;
        if ((mask & LibSocketCan.CAN_CTRLMODE_LISTENONLY) != 0)
            features |= CanFeature.ListenOnly;
        if ((mask & LibSocketCan.CAN_CTRLMODE_FD) != 0)
            features |= CanFeature.CanFd;
        if ((mask & LibSocketCan.CAN_CTRLMODE_BERR_REPORTING) != 0)
            features |= CanFeature.ErrorFrame;
        if (LibSocketCan.can_get_berr_counter(busOptions.ChannelName, out _) == Libc.OK)
            features |= CanFeature.ErrorCounters;
        return new Capability(features);
    }
}
