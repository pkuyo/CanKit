using System.Collections.Generic;
using CanKit.Adapter.ControlCAN.Definitions;
using CanKit.Adapter.ControlCAN.Options;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ControlCAN;

public class ControlCanEProvider(DeviceType type) : ICanModelProvider, ICanCapabilityProvider
{
    public DeviceType DeviceType { get; } = type;

    public virtual CanFeature StaticFeatures => CanFeature.CanClassic | CanFeature.RangeFilter | CanFeature.CyclicTx;

    public ICanFactory Factory => CanRegistry.Registry.Factory("ControlCAN");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var opt = new ControlCanDeviceOptions(this);
        var cfg = new ControlCanDeviceInitOptionsConfigurator();
        cfg.Init(opt);
        return (opt, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
    {
        var opt = new ControlCanBusOptions(this);
        var cfg = new ControlCanBusInitConfigurator();
        cfg.Init(opt);
        return (opt, cfg);
    }

    public Capability QueryCapabilities(IBusOptions busOptions) => new(StaticFeatures, new Dictionary<string, object?>());
}

public class ControlCanProvider(DeviceType type) : ICanModelProvider, ICanCapabilityProvider
{
    public DeviceType DeviceType { get; } = type;

    public virtual CanFeature StaticFeatures => CanFeature.CanClassic | CanFeature.MaskFilter;

    public ICanFactory Factory => CanRegistry.Registry.Factory("ControlCAN");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var opt = new ControlCanDeviceOptions(this);
        var cfg = new ControlCanDeviceInitOptionsConfigurator();
        cfg.Init(opt);
        return (opt, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
    {
        var opt = new ControlCanBusOptions(this);
        var cfg = new ControlCanBusInitConfigurator();
        cfg.Init(opt);
        return (opt, cfg);
    }

    public Capability QueryCapabilities(IBusOptions busOptions) => new(StaticFeatures, new Dictionary<string, object?>());
}





