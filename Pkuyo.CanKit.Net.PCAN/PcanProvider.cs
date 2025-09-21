using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Registry;
using Pkuyo.CanKit.PCAN.Definitions;

namespace Pkuyo.CanKit.Net.PCAN;

public sealed class PcanProvider : ICanModelProvider
{
    public DeviceType DeviceType => PcanDeviceType.PCANBasic;

    // Keep initial scope to classic + filters. FD can be added later.
    public CanFeature StaticFeatures => CanFeature.CanClassic | CanFeature.Filters;

    public ICanFactory Factory => CanRegistry.Registry.Factory("PCAN");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var options = new NullDeviceOptions(this);
        var cfg = new NullDeviceInitOptionsConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions(int channelIndex)
    {
        var options = new PcanBusOptions(this)
        {
            ChannelIndex = channelIndex,
            BitTiming = new BitTiming(500_000, null, null),
            ProtocolMode = CanProtocolMode.Can20,
            WorkMode = ChannelWorkMode.Normal,
            Channel = $"PCAN_USBBUS{Math.Max(1, channelIndex + 1)}"
        };
        var cfg = new PcanBusInitConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }
}
