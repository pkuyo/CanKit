using System;
using System.Linq;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Diagnostics;
using Pkuyo.CanKit.ZLG.Exceptions;

namespace Pkuyo.CanKit.ZLG.Options;

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

public sealed class ZlgChannelInitConfigurator 
    : ChannelInitOptionsConfigurator<ZlgChannelOptions,ZlgChannelInitConfigurator>
{
    public ZlgChannelOptions.MaskFilterType MaskFilterType => Options.FilterType;
    
    /// <summary>
    /// Polling interval in ms (轮询间隔，毫秒)。
    /// </summary>
    public int PollingInterval => Options.PollingInterval;

    /// <summary>
    /// Set polling interval (设置轮询间隔)。
    /// </summary>
    /// <param name="newPollingInterval">Interval in ms (间隔毫秒)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    public ZlgChannelInitConfigurator SetPollingInterval(int newPollingInterval)
    {
        Options.PollingInterval = newPollingInterval;
        return this;
    }

    public ZlgChannelInitConfigurator SetMaskFilterType(ZlgChannelOptions.MaskFilterType maskFilterType)
    {
        Options.FilterType = maskFilterType;
        return this;
    }

    public override ZlgChannelInitConfigurator AccMask(uint accCode, uint accMask, CanFilterIDType idType = CanFilterIDType.Standard)
    {
        if(Filter.FilterRules.Any(i => i is FilterRule.Range))
            throw new CanFilterConfigurationException(
                "ZLG channels only supports the same type of filter rule.");
        
        if (Filter.FilterRules.Count > 1)
            throw new CanFilterConfigurationException(
                "ZLG channels only support a single mask filter rule.");
        
        if(Provider is ZlgCanProvider provider)
            ZlgErr.ThrowIfNotSupport(provider.ZlgFeature, ZlgFeature.MaskFilter);
        
        return base.AccMask(accCode, accMask, idType);
    }

    public override ZlgChannelInitConfigurator RangeFilter(uint min, uint max, CanFilterIDType idType = CanFilterIDType.Standard)
    {
        if(Filter.FilterRules.Any(i => i is FilterRule.Mask))
            throw new CanFilterConfigurationException(
                "ZLG channels only supports the same type of filter rule.");
        
        if(Provider is ZlgCanProvider provider)
            ZlgErr.ThrowIfNotSupport(provider.ZlgFeature, ZlgFeature.RangeFilter);
        
        return base.RangeFilter(min, max, idType);
    }
    
}

public sealed class ZlgChannelRTConfigurator 
    : ChannelRTOptionsConfigurator<ZlgChannelOptions>
{
    public ZlgChannelOptions.MaskFilterType MaskFilterType => Options.FilterType;
    
    /// <summary>
    /// Polling interval in ms (轮询间隔，毫秒)。
    /// </summary>
    public int PollingInterval => Options.PollingInterval;

    /// <summary>
    /// Set polling interval (设置轮询间隔)。
    /// </summary>
    /// <param name="newPollingInterval">Interval in ms (间隔毫秒)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    public ZlgChannelRTConfigurator SetPollingInterval(int newPollingInterval)
    {
        Options.PollingInterval = newPollingInterval;
        return this;
    }
    
}