using System;
using System.ComponentModel;
using System.Linq;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.Core.Utils;

namespace Pkuyo.CanKit.Net.Core.Definitions
{

// 设备 RT（只读接口 + 具体类）
    public class DeviceRTOptionsConfigurator<TDeviceOptions>
        : CallOptionsConfigurator<TDeviceOptions, DeviceRTOptionsConfigurator<TDeviceOptions>>,
            IDeviceRTOptionsConfigurator
        where TDeviceOptions : class, IDeviceOptions
    {
        public ICanModelProvider Provider => Options.Provider;
        public DeviceType DeviceType   => Options.DeviceType;
        public uint       TxTimeOut    => Options.TxTimeOut;
        
    }

    
    
      public class ChannelRTOptionsConfigurator<TChannelOptions>
        : CallOptionsConfigurator<TChannelOptions, ChannelRTOptionsConfigurator<TChannelOptions>>,
          IChannelRTOptionsConfigurator
        where TChannelOptions : class, IChannelOptions
    {
        public ICanModelProvider Provider => Options.Provider;
        public int ChannelIndex => Options.ChannelIndex;
        public BitTiming BitTiming => Options.BitTiming;
        public TxRetryPolicy TxRetryPolicy => Options.TxRetryPolicy;
        public bool BusUsageEnabled => Options.BusUsageEnabled;
        public uint  BusUsagePeriodTime => Options.BusUsagePeriodTime;
        public ChannelWorkMode WorkMode  => Options.WorkMode;
        public bool InternalResistance => Options.InternalResistance;
        public CanProtocolMode ProtocolMode => Options.ProtocolMode;
        public ICanFilter Filter => Options.Filter;
        
        public ChannelRTOptionsConfigurator<TChannelOptions> SetInternalResistance(bool enabled)
        {
            Options.InternalResistance = enabled;
            return Self;
        }
    }
      
    public class DeviceInitOptionsConfigurator<TDeviceOptions,TSelf>
        : CallOptionsConfigurator<TDeviceOptions, DeviceInitOptionsConfigurator<TDeviceOptions,TSelf>>,
            IDeviceInitOptionsConfigurator
        where TDeviceOptions : class, IDeviceOptions
        where TSelf : DeviceInitOptionsConfigurator<TDeviceOptions, TSelf>
    {
        public ICanModelProvider Provider => Options.Provider;
        public DeviceType DeviceType => Options.DeviceType;
        public uint TxTimeOutTime => Options.TxTimeOut;

        public TSelf TxTimeOut(uint ms)
        {
            Options.TxTimeOut = ms;
            return (TSelf)this;
        }

        
        
        IDeviceInitOptionsConfigurator IDeviceInitOptionsConfigurator.TxTimeOut(uint ms)
            => TxTimeOut(ms);
        
    }

  public class ChannelInitOptionsConfigurator<TChannelOptions, TSelf>
    : CallOptionsConfigurator<TChannelOptions, ChannelInitOptionsConfigurator<TChannelOptions,TSelf>>,
      IChannelInitOptionsConfigurator
    where TChannelOptions : class, IChannelOptions
    where TSelf : ChannelInitOptionsConfigurator<TChannelOptions, TSelf>
    {
 
        public ICanModelProvider Provider => Options.Provider;
        public int ChannelIndex => Options.ChannelIndex;
        public BitTiming BitTiming => Options.BitTiming;
        public TxRetryPolicy TxRetryPolicy => Options.TxRetryPolicy;
        public bool BusUsageEnabled => Options.BusUsageEnabled;
        public uint BusUsagePeriodTime => Options.BusUsagePeriodTime;
        public ChannelWorkMode WorkMode => Options.WorkMode;
        public bool InternalResistance => Options.InternalResistance;
        public CanProtocolMode ProtocolMode => Options.ProtocolMode;
        
        public ICanFilter Filter => Options.Filter;


        public TSelf Baud(uint baud)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanClassic);
            Options.BitTiming = new BitTiming(BaudRate: baud);
            return (TSelf)this;
        }

        public TSelf Fd(uint abit, uint dbit)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanFd);
            Options.BitTiming = new BitTiming(ArbitrationBitRate: abit, DataBitRate: dbit);
            return (TSelf)this;
        }

        public TSelf BusUsage(uint periodMs = 1000)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.BusUsage);
            Options.BusUsageEnabled = true;
            Options.BusUsagePeriodTime = periodMs;
            return (TSelf)this;
        }

        public TSelf SetTxRetryPolicy(TxRetryPolicy retryPolicy)
        {
            Options.TxRetryPolicy = retryPolicy;
            return (TSelf)this;
        }

        public TSelf SetWorkMode(ChannelWorkMode mode)
        {
            Options.WorkMode = mode;
            return (TSelf)this;
        }

        public TSelf InternalRes(bool enabled)
        {
            Options.InternalResistance = enabled;
            return (TSelf)this;
        }

        public TSelf SetProtocolMode(CanProtocolMode mode)
        {
            Options.ProtocolMode = mode;
            Options.BitTiming = mode switch
            {
                CanProtocolMode.Can20 => new BitTiming(),
                _ => new BitTiming(null, 500_000, 500_000)
            };
            return (TSelf)this;
        }

        public TSelf SetFilter(CanFilter filter)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.Filters);
            
            Options.Filter = filter;
            return (TSelf)this;
        }

        public TSelf RangeFilter(uint min, uint max, CanFilterIDType idType = CanFilterIDType.Standard)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.Filters);
            if (min > max)
            {
                throw new ArgumentException($"Invalid range: min ({min}) must be less than or equal to max ({max}).",
                    nameof(max));
            }
            Options.Filter ??= new CanFilter();
            Options.Filter.filterRules.Add(new FilterRule.Range(min, max, idType));
            return (TSelf)this;
            
        }

        public TSelf AccMask(uint accCode, uint accMask, CanFilterIDType idType = CanFilterIDType.Standard)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.Filters);
            
            Options.Filter ??= new CanFilter();
            Options.Filter.filterRules.Add(new FilterRule.Mask(accCode, accMask, idType));
            return (TSelf)this;
        }

        IChannelInitOptionsConfigurator IChannelInitOptionsConfigurator.Baud(uint baud) 
            => Baud(baud);

        IChannelInitOptionsConfigurator IChannelInitOptionsConfigurator.Fd(uint abit, uint dbit) 
            => Fd(abit, dbit);

        IChannelInitOptionsConfigurator IChannelInitOptionsConfigurator.BusUsage(uint periodMs) 
            => BusUsage(periodMs);

        IChannelInitOptionsConfigurator IChannelInitOptionsConfigurator.SetTxRetryPolicy(TxRetryPolicy retryPolicy) 
            => SetTxRetryPolicy(retryPolicy);

        IChannelInitOptionsConfigurator IChannelInitOptionsConfigurator.SetWorkMode(ChannelWorkMode mode) 
            => SetWorkMode(mode);

        IChannelInitOptionsConfigurator IChannelInitOptionsConfigurator.InternalRes(bool enabled) 
            => InternalRes(enabled);

        IChannelInitOptionsConfigurator IChannelInitOptionsConfigurator.SetProtocolMode(CanProtocolMode mode)
            => SetProtocolMode(mode);

        IChannelInitOptionsConfigurator IChannelInitOptionsConfigurator.SetFilter(CanFilter filter)
            => SetFilter(filter);
        
        IChannelInitOptionsConfigurator IChannelInitOptionsConfigurator.RangeFilter(uint min, uint max, CanFilterIDType idType)
            => RangeFilter(min, max, idType);
        
        IChannelInitOptionsConfigurator IChannelInitOptionsConfigurator.AccMask(uint accCode, uint accMask, CanFilterIDType idType)
            => AccMask(accCode, accMask, idType);
    }

}