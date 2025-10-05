using CanKit.Adapter.PCAN.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.PCAN;

public sealed class PcanProvider : ICanModelProvider
{
    public DeviceType DeviceType => PcanDeviceType.PCANBasic;

    // FD depends on hardware (sniffed at runtime).
    public CanFeature StaticFeatures => CanFeature.CanClassic | CanFeature.Filters;

    public ICanFactory Factory => CanRegistry.Registry.Factory("PCAN");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var options = new NullDeviceOptions(this);
        var cfg = new NullDeviceInitOptionsConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
    {
        var options = new PcanBusOptions(this)
        {
            BitTiming = CanBusTiming.ClassicDefault(),
            ProtocolMode = CanProtocolMode.Can20,
            WorkMode = ChannelWorkMode.Normal,
            ChannelName = $"PCAN_USBBUS1"
        };
        var cfg = new PcanBusInitConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }
}
