using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.Virtual.Definitions;
using CanKit.Core.Definitions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Virtual;

public sealed class VirtualProvider : ICanModelProvider
{
    public DeviceType DeviceType => VirtualDeviceType.Virtual;

    // Every real adapter (SocketCAN, Kvaser, ZLG, PCAN, Vector) declares CanFeature.Echo as a
    // static hardware capability; Virtual genuinely has the same capability via
    // ChannelWorkMode.Echo (see VirtualBusHub.Broadcast), it was just never declared here. This
    // matters beyond consistency: it's the only way hardware-independent CI (which only ever runs
    // against Virtual) can exercise any capability-gated echo behavior at all.
    public CanFeature StaticFeatures => CanFeature.CanClassic | CanFeature.CanFd | CanFeature.RangeFilter | CanFeature.MaskFilter |
                                        CanFeature.ErrorFrame | CanFeature.ErrorCounters | CanFeature.CyclicTx | CanFeature.Echo;

    public ICanFactory Factory => CanRegistry.Registry.Factory("Virtual");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var options = new NullDeviceOptions(this);
        var cfg = new NullDeviceInitOptionsConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
    {
        var options = new VirtualBusOptions(this)
        {
            BitTiming = CanBusTiming.ClassicDefault(),
            ProtocolMode = CanProtocolMode.Can20,
            WorkMode = ChannelWorkMode.Normal,
            ChannelName = $"virtual0"
        };
        var cfg = new VirtualBusInitConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }
}

