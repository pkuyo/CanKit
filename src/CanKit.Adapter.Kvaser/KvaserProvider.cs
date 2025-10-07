using CanKit.Adapter.Kvaser.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Kvaser;

public sealed class KvaserProvider : ICanModelProvider
{
    public DeviceType DeviceType => KvaserDeviceType.CANlib;

    public CanFeature StaticFeatures => CanFeature.All;

    public ICanFactory Factory => CanRegistry.Registry.Factory("KVASER");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var options = new NullDeviceOptions(this);
        var cfg = new NullDeviceInitOptionsConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
    {
        var options = new KvaserBusOptions(this)
        {
            BitTiming = CanBusTiming.ClassicDefault(),
            ProtocolMode = CanProtocolMode.Can20,
            WorkMode = ChannelWorkMode.Normal
        };
        var cfg = new KvaserBusInitConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }
}
