using System;
using System.Linq;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.ControlCAN.Options;

public sealed class ControlCanDeviceInitOptionsConfigurator
    : DeviceInitOptionsConfigurator<ControlCanDeviceOptions, ControlCanDeviceInitOptionsConfigurator>
{
    public ControlCanDeviceInitOptionsConfigurator DeviceIndex(uint deviceIndex)
    {
        Options.DeviceIndex = deviceIndex;
        return this;
    }
}

public sealed class ControlCanDeviceRTOptionsConfigurator
    : DeviceRTOptionsConfigurator<ControlCanDeviceOptions>
{
    public uint DeviceIndex => Options.DeviceIndex;
}

public sealed class ControlCanBusInitConfigurator
    : BusInitOptionsConfigurator<ControlCanBusOptions, ControlCanBusInitConfigurator>
{
    public int PollingInterval => Options.PollingInterval;

    public override ControlCanBusInitConfigurator AccMask(int accCode, int accMask, CanFilterIDType idType = CanFilterIDType.Standard)
    {
        if ((EnabledSoftwareFallback & CanFeature.MaskFilter) == 0)
        {
            if (Filter.FilterRules.Any(i => i is FilterRule.Range))
                throw new CanFilterConfigurationException(
                    "ControlCAN only supports mask filter on hardware");
        }
        return base.AccMask(accCode, accMask, idType);
    }
}

public sealed class ControlCanBusRtConfigurator
    : BusRtOptionsConfigurator<ControlCanBusOptions, ControlCanBusRtConfigurator>
{
    public int PollingInterval
    {
        get => Options.PollingInterval;
        set => Options.PollingInterval = value;
    }
}

