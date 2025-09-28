using CanKit.Adapter.SocketCAN.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.SocketCAN;

public sealed class SocketCanProvider : ICanModelProvider
{
    public DeviceType DeviceType => LinuxDeviceType.SocketCAN;

    public CanFeature StaticFeatures => CanFeature.CanClassic | CanFeature.CanFd | CanFeature.Filters |
                                        CanFeature.ErrorFrame | CanFeature.ErrorCounters | CanFeature.CyclicTx;

    public ICanFactory Factory => CanRegistry.Registry.Factory("SocketCAN");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var options = new NullDeviceOptions(this);
        var cfg = new NullDeviceInitOptionsConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions(int channelIndex)
    {
        var options = new SocketCanBusOptions(this)
        {
            ChannelIndex = channelIndex,
            BitTiming = new BitTiming(new CanTimingConfig(500_000), null),
            ProtocolMode = CanProtocolMode.Can20,
            WorkMode = ChannelWorkMode.Normal,
            InterfaceName = $"can{channelIndex}"
        };
        var cfg = new SocketCanBusInitConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }
}
