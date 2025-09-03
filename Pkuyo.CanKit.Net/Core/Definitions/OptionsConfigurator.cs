using System;
using System.ComponentModel;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Utils;

namespace Pkuyo.CanKit.Net.Core.Definitions
{

// 设备 RT（只读接口 + 具体类）
    public class DeviceRTOptionsConfigurator<TDeviceOptions>
        : CallOptionsConfigurator<TDeviceOptions, DeviceRTOptionsConfigurator<TDeviceOptions>>,
            IDeviceRTOptionsConfigurator
        where TDeviceOptions : class, IDeviceOptions
    {
        public ICanModelProvider Provider => _options.Provider;
        public DeviceType DeviceType   => _options.DeviceType;
        public uint       TxTimeOut    => _options.TxTimeOut;
        
    }

    
    
      public class ChannelRTOptionsConfigurator<TChannelOptions>
        : CallOptionsConfigurator<TChannelOptions, ChannelRTOptionsConfigurator<TChannelOptions>>,
          IChannelRTOptionsConfigurator
        where TChannelOptions : class, IChannelOptions
    {
        public ICanModelProvider Provider => _options.Provider;
        public int            ChannelIndex        => _options.ChannelIndex;
        public BitTiming      BitTiming           => _options.BitTiming;
        public TxRetryPolicy  TxRetryPolicy       => _options.TxRetryPolicy;
        public bool           BusUsageEnabled     => _options.BusUsageEnabled;
        public uint           BusUsagePeriodTime  => _options.BusUsagePeriodTime;
        public ChannelWorkMode WorkMode           => _options.WorkMode;
        public bool           InternalResistance  => _options.InternalResistance;
        public CanProtocolMode ProtocolMode       => _options.ProtocolMode;


        public ChannelRTOptionsConfigurator<TChannelOptions> SetInternalResistance(bool enabled)
        {
            _options.InternalResistance = enabled;
            return Self;
        }
    }
      
    public class DeviceInitOptionsConfigurator<TDeviceOptions,TSelf>
        : CallOptionsConfigurator<TDeviceOptions, DeviceInitOptionsConfigurator<TDeviceOptions,TSelf>>,
            IDeviceInitOptionsConfigurator
        where TDeviceOptions : class, IDeviceOptions
        where TSelf : DeviceInitOptionsConfigurator<TDeviceOptions, TSelf>
    {
        public ICanModelProvider Provider => _options.Provider;
        public DeviceType DeviceType => _options.DeviceType;
        public uint TxTimeOutTime => _options.TxTimeOut;

        public TSelf TxTimeOut(uint ms)
        {
            _options.TxTimeOut = ms;
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
 
        public ICanModelProvider Provider => _options.Provider;
        public int ChannelIndex => _options.ChannelIndex;
        public BitTiming BitTiming => _options.BitTiming;
        public TxRetryPolicy TxRetryPolicy => _options.TxRetryPolicy;
        public bool BusUsageEnabled => _options.BusUsageEnabled;
        public uint BusUsagePeriodTime => _options.BusUsagePeriodTime;
        public ChannelWorkMode WorkMode => _options.WorkMode;
        public bool InternalResistance => _options.InternalResistance;
        public CanProtocolMode ProtocolMode { get; }


        public TSelf Baud(uint baud)
        {
            _feature.CheckFeature(CanFeature.CanClassic);
            _options.BitTiming = new BitTiming(BaudRate: baud);
            return (TSelf)this;
        }

        public TSelf Fd(uint abit, uint dbit)
        {
            _feature.CheckFeature(CanFeature.CanFd);
            _options.BitTiming = new BitTiming(ArbitrationBitRate: abit, DataBitRate: dbit);
            return (TSelf)this;
        }

        public TSelf BusUsage(uint periodMs = 1000)
        {
            _feature.CheckFeature(CanFeature.BusUsage);
            _options.BusUsageEnabled = true;
            _options.BusUsagePeriodTime = periodMs;
            return (TSelf)this;
        }

        public TSelf SetTxRetryPolicy(TxRetryPolicy retryPolicy)
        {
            _options.TxRetryPolicy = retryPolicy;
            return (TSelf)this;
        }

        public TSelf SetWorkMode(ChannelWorkMode mode)
        {
            _options.WorkMode = mode;
            return (TSelf)this;
        }

        public TSelf InternalRes(bool enabled)
        {
            _options.InternalResistance = enabled;
            return (TSelf)this;
        }

        public TSelf SetProtocolMode(CanProtocolMode mode)
        {
            _options.WorkMode = WorkMode;
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
    }

}