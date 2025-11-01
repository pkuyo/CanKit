using System;
using System.Linq;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
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
    /// <summary>
    /// Set polling interval (设置轮询间隔)。
    /// </summary>
    /// <param name="newPollingInterval">Interval in ms (间隔毫秒)。</param>
    public ControlCanBusInitConfigurator PollingInterval(int newPollingInterval)
    {
        if (newPollingInterval < 0)
            throw new ArgumentOutOfRangeException(nameof(newPollingInterval));
        Options.PollingInterval = newPollingInterval;
        return this;
    }

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

    public override IBusInitOptionsConfigurator Custom(string key, object value)
    {
        switch (key)
        {
            case nameof(PollingInterval):
                Options.PollingInterval = Convert.ToInt32(value);
                break;
            default:
                CanKitLogger.LogWarning($"ControlCAN: invalid key: {key}");
                break;
        }
        return this;
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

