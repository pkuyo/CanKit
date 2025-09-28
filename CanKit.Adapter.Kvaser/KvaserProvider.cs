using CanKit.Adapter.Kvaser.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Kvaser;

public sealed class KvaserProvider : ICanModelProvider
{
    public DeviceType DeviceType => KvaserDeviceType.CANlib;

    public CanFeature StaticFeatures => CanFeature.CanClassic | CanFeature.Filters | CanFeature.ErrorCounters;

    public ICanFactory Factory => CanRegistry.Registry.Factory("KVASER");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var options = new NullDeviceOptions(this);
        var cfg = new NullDeviceInitOptionsConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions(int channelIndex)
    {
        var options = new KvaserBusOptions(this)
        {
            ChannelIndex = channelIndex,
            BitTiming = new BitTiming(500_000),
            ProtocolMode = CanProtocolMode.Can20,
            WorkMode = ChannelWorkMode.Normal,
            ChannelNumber = channelIndex
        };
        var cfg = new KvaserBusInitConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }
}

