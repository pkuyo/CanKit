using System;
using System.ComponentModel;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Internal;
using Pkuyo.CanKit.Net.Core.Utils;

namespace Pkuyo.CanKit.Net.Core.Definitions
{

// 设备 RT（只读接口 + 具体类）
    public class DeviceRTOptionsConfigurator<T>
        : CallOptionsConfigurator<T, DeviceRTOptionsConfigurator<T>>,
            IDeviceRTOptionsConfigurator<T>
        where T : class, IDeviceOptions
    {
        public DeviceType DeviceType   => _options.DeviceType;
        public uint       TxTimeOut    => _options.TxTimeOut;
        public bool       MergeReceive => _options.MergeReceive;
        
    }

    
    
      public class ChannelRTOptionsConfigurator<T>
        : CallOptionsConfigurator<T, ChannelRTOptionsConfigurator<T>>,
          IChannelRTOptionsConfigurator<T>
        where T : class, IChannelOptions
    {
        
        public int            ChannelIndex        => _options.ChannelIndex;
        public BitTiming      BitTiming           => _options.BitTiming;
        public TxRetryPolicy  TxRetryPolicy       => _options.TxRetryPolicy;
        public bool           BusUsageEnabled     => _options.BusUsageEnabled;
        public uint           BusUsagePeriodTime  => _options.BusUsagePeriodTime;
        public ChannelWorkMode WorkMode           => _options.WorkMode;
        public bool           InternalResistance  => _options.InternalResistance;


        public ChannelRTOptionsConfigurator<T> SetInternalResistance(bool enabled)
        {
            _options.InternalResistance = enabled;
            return Self;
        }
    }
      
    public class DeviceInitOptionsConfigurator<T>
        : CallOptionsConfigurator<T, DeviceInitOptionsConfigurator<T>>,
            IDeviceInitOptionsConfigurator<T>,
            IDeviceInitOptionsMutable<DeviceInitOptionsConfigurator<T>, T>
        where T : class, IDeviceOptions
    {
        // 协变只读
        public DeviceType DeviceType => _options.DeviceType;
        public uint TxTimeOutTime => _options.TxTimeOut;
        public bool EnableMergeReceive => _options.MergeReceive;

       
        public new DeviceInitOptionsConfigurator<T> Init(T options, CanFeature feature)
            => (DeviceInitOptionsConfigurator<T>)base.Init(options, feature);

        public DeviceInitOptionsConfigurator<T> TxTimeOut(uint ms)
        {
            _options.TxTimeOut = ms;
            return this;
        }

        public DeviceInitOptionsConfigurator<T> MergeReceive(bool enable)
        {
            _options.MergeReceive = enable;
            return this;
        }
    }

  public class ChannelInitOptionsConfigurator<T>
    : CallOptionsConfigurator<T, ChannelInitOptionsConfigurator<T>>,
      IChannelInitOptionsConfigurator<T>,             
      IChannelInitOptionsMutable<ChannelInitOptionsConfigurator<T>, T> 
    where T : class, IChannelOptions
    {
 
        public int ChannelIndex => _options.ChannelIndex;
        public BitTiming BitTiming => _options.BitTiming;
        public TxRetryPolicy TxRetryPolicy => _options.TxRetryPolicy;
        public bool BusUsageEnabled => _options.BusUsageEnabled;
        public uint BusUsagePeriodTime => _options.BusUsagePeriodTime;
        public ChannelWorkMode WorkMode => _options.WorkMode;
        public bool InternalResistance => _options.InternalResistance;

        // --- 可变（链式接口部分） ---
        public new ChannelInitOptionsConfigurator<T> Init(T options, CanFeature feature)
            => (ChannelInitOptionsConfigurator<T>)base.Init(options, feature);

        public ChannelInitOptionsConfigurator<T> Baud(uint baud)
        {
            _feature.CheckFeature(CanFeature.CanClassic);
            _options.BitTiming = new BitTiming(BaudRate: baud);
            return this;
        }

        public ChannelInitOptionsConfigurator<T> Fd(uint abit, uint dbit)
        {
            _feature.CheckFeature(CanFeature.CanFd);
            _options.BitTiming = new BitTiming(ArbitrationBitRate: abit, DataBitRate: dbit);
            return this;
        }

        public ChannelInitOptionsConfigurator<T> BusUsage(uint periodMs = 1000)
        {
            _feature.CheckFeature(CanFeature.BusUsage);
            _options.BusUsageEnabled = true;
            _options.BusUsagePeriodTime = periodMs;
            return this;
        }

        public ChannelInitOptionsConfigurator<T> SetTxRetryPolicy(TxRetryPolicy retryPolicy)
        {
            _options.TxRetryPolicy = retryPolicy;
            return this;
        }

        public ChannelInitOptionsConfigurator<T> SetWorkMode(ChannelWorkMode mode)
        {
            _options.WorkMode = mode;
            return this;
        }

        public ChannelInitOptionsConfigurator<T> InternalRes(bool enabled)
        {
            _options.InternalResistance = enabled;
            return this;
        }
}

}