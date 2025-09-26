using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Registry;
using Pkuyo.CanKit.SocketCAN.Definitions;

namespace Pkuyo.CanKit.Net.SocketCAN;

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
            BitTiming = new BitTiming(500_000),
            ProtocolMode = CanProtocolMode.Can20,
            WorkMode = ChannelWorkMode.Normal,
            InterfaceName = $"can{channelIndex}"
        };
        var cfg = new SocketCanBusInitConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }
}
