using System;
using System.ComponentModel;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Internal;
using Pkuyo.CanKit.Net.Core.Utils;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    
    public class ChannelRTOptionsConfigurator(IChannelOptions options, CanFeature canFeature)
    {
        
        public int ChannelIndex => options.ChannelIndex;

        public BitTiming BitTiming => options.BitTiming;
        
        public TxRetryPolicy TxRetryPolicy => options.TxRetryPolicy;
        
        public bool BusUsageEnabled => options.BusUsageEnabled;
        
        public uint BusUsagePeriodTime => options.BusUsagePeriodTime;
        
        public ChannelWorkMode WorkMode => options.WorkMode;
            
        public bool InternalResistance
        {
            get => options.InternalResistance;
            set => options.InternalResistance = value;
        }
    }

  

    public class ChannelInitOptionsConfigurator(IChannelOptions options,CanFeature canFeature)
    {
        public ChannelInitOptionsConfigurator Baud(uint baud)
        {
            canFeature.CheckFeature(CanFeature.CanClassic);
            options.BitTiming = new BitTiming(BaudRate:baud);
            return this;
        }
        public ChannelInitOptionsConfigurator Fd(uint abit, uint dbit)
        {
            canFeature.CheckFeature(CanFeature.CanFd);
            options.BitTiming = new BitTiming(ArbitrationBitRate:abit, DataBitRate:dbit);
            return this;
        }
        
        // BusUsage
        public ChannelInitOptionsConfigurator BusUsage(uint periodMs = 1000)
        {
            canFeature.CheckFeature(CanFeature.BusUsage);
            options.BusUsageEnabled = true;
            options.BusUsagePeriodTime = periodMs;
            return this;
        }

        public ChannelInitOptionsConfigurator TxRetryPolicy(TxRetryPolicy retryPolicy)
        {
            options.TxRetryPolicy = retryPolicy;
            return this;
        }

        public ChannelInitOptionsConfigurator WorkMode(ChannelWorkMode mode)
        {
            options.WorkMode = mode;
            return this;
        }
        // TODO: Ethernet
        /*
        public ChannelInitOptionsConfigurator Ethernet(string ip, int localPort, EthernetWorkMode mode)
        {
            canFeature.CheckFeature(CanFeature.Ethernet);
            return this;
        }
        */
    }

    public class DeviceInitOptionsConfigurator(IDeviceOptions options, CanFeature canFeature)
    {
        public DeviceInitOptionsConfigurator TxTimeOut(uint ms)
        {
            return this;
        }
        public DeviceInitOptionsConfigurator MergeReceive(bool enable)
        {
            return this;
        }
    }
#pragma warning disable 0618  
    
    public class DeviceRTOptionsConfigurator(IDeviceOptions options, CanFeature canFeature)
    {
        public uint TxTimeOut => options.TxTimeOut;
        
        public bool MergeReceive => options.MergeReceive;
    }
    
    public class ChannelRTOptionsConfigurator<T>(T options, CanFeature canFeature)
        : ChannelRTOptionsConfigurator(options, canFeature), IOptionsAccessor<T> where T : class, IChannelOptions
    {
        [Obsolete("Access ICanOptions may caused unexpected error.", error: false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        T IOptionsAccessor<T>.Options => options;
    }
    
    public class DeviceRTOptionsConfigurator<T>(T options, CanFeature canFeature)
        : DeviceRTOptionsConfigurator(options, canFeature), IOptionsAccessor<T> where T : class, IDeviceOptions
    {
        [Obsolete("Access ICanOptions may caused unexpected error.", error: false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        T IOptionsAccessor<T>.Options => options;
    }
    
    public class ChannelInitOptionsConfigurator<T>(T options, CanFeature canFeature)
        : ChannelInitOptionsConfigurator(options, canFeature), IOptionsAccessor<T> where T : class, IChannelOptions
    {
        
        [Obsolete("Access ICanOptions may caused unexpected error.", error: false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        T IOptionsAccessor<T>.Options => options;
    }
    
    public class DeviceInitOptionsConfigurator<T>(T options, CanFeature canFeature)
        : DeviceInitOptionsConfigurator(options, canFeature), IOptionsAccessor<T> where T : class, IDeviceOptions
    {
        [Obsolete("Access ICanOptions may caused unexpected error.", error: false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        T IOptionsAccessor<T>.Options => options;
    }
    
#pragma warning restore 0618

}