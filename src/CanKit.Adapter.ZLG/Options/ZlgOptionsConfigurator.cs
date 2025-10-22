using System;
using System.Linq;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Diagnostics;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.ZLG.Options;

public sealed class ZlgDeviceInitOptionsConfigurator
    : DeviceInitOptionsConfigurator<ZlgDeviceOptions, ZlgDeviceInitOptionsConfigurator>
{
    public ZlgDeviceInitOptionsConfigurator DeviceIndex(uint deviceIndex)
    {
        Options.DeviceIndex = deviceIndex;
        return this;
    }
}

public sealed class ZlgDeviceRTOptionsConfigurator
    : DeviceRTOptionsConfigurator<ZlgDeviceOptions>
{
    public uint DeviceIndex => Options.DeviceIndex;
}

public sealed class ZlgBusInitConfigurator
    : BusInitOptionsConfigurator<ZlgBusOptions, ZlgBusInitConfigurator>
{

    public bool EnableMergeReceive => Options.MergeReceive ?? false;


    /// <summary>
    /// Set polling interval (设置轮询间隔)。
    /// </summary>
    /// <param name="newPollingInterval">Interval in ms (间隔毫秒)。</param>
    public ZlgBusInitConfigurator PollingInterval(int newPollingInterval)
    {
        Options.PollingInterval = newPollingInterval;
        return this;
    }
    /// <summary>
    /// Enable or disable merge receive （启用或禁用mergeReceive）
    /// </summary>
    /// <param name="newEnable">新的启用状态（全设备使用）。</param>
    public ZlgBusInitConfigurator MergeReceive(bool newEnable)
    {
        ZlgErr.ThrowIfNotSupport(ZlgFeatures, ZlgFeature.MergeReceive);
        Options.MergeReceive = newEnable;
        return this;
    }

    public override ZlgBusInitConfigurator AccMask(int accCode, int accMask, CanFilterIDType idType = CanFilterIDType.Standard)
    {
        if ((EnabledSoftwareFallback & CanFeature.MaskFilter) == 0)
        {
            if (Filter.FilterRules.Any(i => i is FilterRule.Range))
                throw new CanFilterConfigurationException(
                    "ZLG channels only supports one mask filter on hardware");
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
                CanKitLogger.LogWarning($"ZLG: invalid key: {key}");
                break;
        }
        return this;
    }

    public override ZlgBusInitConfigurator SetFilter(CanFilter filter)
    {
        if ((Features & CanFeature.MaskFilter) != 0 && filter.FilterRules.Count(i => i is FilterRule.Mask) > 1)
        {
            throw new CanFilterConfigurationException(
                "ZLG channels only supports one mask filter on hardware");
        }

        return base.SetFilter(filter);
    }

    public ZlgFeature ZlgFeatures => Options.ZlgFeatures;
}

public sealed class ZlgBusRtConfigurator
    : BusRtOptionsConfigurator<ZlgBusOptions, ZlgBusRtConfigurator>
{

    /// <summary>
    /// Polling interval in ms (轮询间隔，毫秒)。
    /// </summary>
    public int PollingInterval
    {
        get => Options.PollingInterval;
        set => Options.PollingInterval = value;
    }

    /// <summary>
    /// merge receive, one device only call one times (合并接收，一个设备只需要启用一次)
    /// </summary>
    public bool? MergeReceive => Options.MergeReceive;

    public ZlgFeature ZlgFeatures => Options.ZlgFeatures;
}
