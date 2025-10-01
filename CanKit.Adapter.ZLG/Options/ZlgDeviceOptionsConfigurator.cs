using System.Linq;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Diagnostics;
using CanKit.Core.Definitions;
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
    public ZlgBusOptions.MaskFilterType MaskFilterType => Options.FilterType;

    /// <summary>
    /// Polling interval in ms (轮询间隔，毫秒)。
    /// </summary>
    public int PollingInterval => Options.PollingInterval;

    /// <summary>
    /// Set polling interval (设置轮询间隔)。
    /// </summary>
    /// <param name="newPollingInterval">Interval in ms (间隔毫秒)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    public ZlgBusInitConfigurator SetPollingInterval(int newPollingInterval)
    {
        Options.PollingInterval = newPollingInterval;
        return this;
    }

    public ZlgBusInitConfigurator SetMaskFilterType(ZlgBusOptions.MaskFilterType maskFilterType)
    {
        Options.FilterType = maskFilterType;
        return this;
    }

    public override ZlgBusInitConfigurator AccMask(uint accCode, uint accMask, CanFilterIDType idType = CanFilterIDType.Standard)
    {
        if ((EnabledSoftwareFallback & CanFeature.Filters) == 0)
        {
            if (Filter.FilterRules.Any(i => i is FilterRule.Range))
                throw new CanFilterConfigurationException(
                    "ZLG channels only supports the same type of filter rule.(without software filter)");

            if (Filter.FilterRules.Count > 1)
                throw new CanFilterConfigurationException(
                    "ZLG channels only support a single mask filter rule.(without software filter)");

            if (Provider is ZlgCanProvider provider)
                ZlgErr.ThrowIfNotSupport(provider.ZlgFeature, ZlgFeature.MaskFilter);
        }
        return base.AccMask(accCode, accMask, idType);
    }

    public override ZlgBusInitConfigurator RangeFilter(uint min, uint max, CanFilterIDType idType = CanFilterIDType.Standard)
    {
        if ((EnabledSoftwareFallback & CanFeature.Filters) == 0)
        {
            if (Filter.FilterRules.Any(i => i is FilterRule.Mask))
                throw new CanFilterConfigurationException(
                    "ZLG channels only supports the same type of filter rule.(without software filter)");

            if (Provider is ZlgCanProvider provider)
                ZlgErr.ThrowIfNotSupport(provider.ZlgFeature, ZlgFeature.RangeFilter);
        }

        return base.RangeFilter(min, max, idType);
    }
}

public sealed class ZlgBusRtConfigurator
    : BusRtOptionsConfigurator<ZlgBusOptions>
{
    public ZlgBusOptions.MaskFilterType MaskFilterType => Options.FilterType;

    /// <summary>
    /// Polling interval in ms (轮询间隔，毫秒)。
    /// </summary>
    public int PollingInterval => Options.PollingInterval;

    /// <summary>
    /// Set polling interval (设置轮询间隔)。
    /// </summary>
    /// <param name="newPollingInterval">Interval in ms (间隔毫秒)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    public ZlgBusRtConfigurator SetPollingInterval(int newPollingInterval)
    {
        Options.PollingInterval = newPollingInterval;
        return this;
    }
}
