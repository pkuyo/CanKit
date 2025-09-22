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
        public CanFeature Features => _feature;
        public DeviceType DeviceType => Options.DeviceType;
        public uint TxTimeOut => Options.TxTimeOut;

    }



    public class BusRtOptionsConfigurator<TChannelOptions>
      : CallOptionsConfigurator<TChannelOptions, BusRtOptionsConfigurator<TChannelOptions>>,
        IBusRTOptionsConfigurator
      where TChannelOptions : class, IBusOptions
    {
        public ICanModelProvider Provider => Options.Provider;
        public CanFeature Features => _feature;
        public int ChannelIndex => Options.ChannelIndex;
        public BitTiming BitTiming => Options.BitTiming;
        public TxRetryPolicy TxRetryPolicy => Options.TxRetryPolicy;
        public bool BusUsageEnabled => Options.BusUsageEnabled;
        public uint BusUsagePeriodTime => Options.BusUsagePeriodTime;
        public ChannelWorkMode WorkMode => Options.WorkMode;
        public bool InternalResistance => Options.InternalResistance;
        public CanProtocolMode ProtocolMode => Options.ProtocolMode;
        public ICanFilter Filter => Options.Filter;
        public bool AllowErrorInfo => Options.AllowErrorInfo;

        public virtual BusRtOptionsConfigurator<TChannelOptions> SetInternalResistance(bool enabled)
        {
            Options.InternalResistance = enabled;
            return Self;
        }
    }

    public class DeviceInitOptionsConfigurator<TDeviceOptions, TSelf>
        : CallOptionsConfigurator<TDeviceOptions, DeviceInitOptionsConfigurator<TDeviceOptions, TSelf>>,
            IDeviceInitOptionsConfigurator
        where TDeviceOptions : class, IDeviceOptions
        where TSelf : DeviceInitOptionsConfigurator<TDeviceOptions, TSelf>
    {
        public ICanModelProvider Provider => Options.Provider;
        public CanFeature Features => _feature;
        public DeviceType DeviceType => Options.DeviceType;
        public uint TxTimeOutTime => Options.TxTimeOut;

        public virtual TSelf TxTimeOut(uint ms)
        {
            Options.TxTimeOut = ms;
            return (TSelf)this;
        }



        IDeviceInitOptionsConfigurator IDeviceInitOptionsConfigurator.TxTimeOut(uint ms)
            => TxTimeOut(ms);

    }

    public class BusInitOptionsConfigurator<TChannelOptions, TSelf>
      : CallOptionsConfigurator<TChannelOptions, BusInitOptionsConfigurator<TChannelOptions, TSelf>>,
        IBusInitOptionsConfigurator
      where TChannelOptions : class, IBusOptions
      where TSelf : BusInitOptionsConfigurator<TChannelOptions, TSelf>
    {

        public ICanModelProvider Provider => Options.Provider;
        public CanFeature Features => _feature;
        public int ChannelIndex => Options.ChannelIndex;
        public BitTiming BitTiming => Options.BitTiming;
        public TxRetryPolicy TxRetryPolicy => Options.TxRetryPolicy;
        public bool BusUsageEnabled => Options.BusUsageEnabled;
        public uint BusUsagePeriodTime => Options.BusUsagePeriodTime;
        public ChannelWorkMode WorkMode => Options.WorkMode;
        public bool InternalResistance => Options.InternalResistance;
        public CanProtocolMode ProtocolMode => Options.ProtocolMode;

        public ICanFilter Filter => Options.Filter;
        public bool AllowErrorInfo { get; }


        public virtual TSelf Baud(uint baud)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanClassic);
            Options.BitTiming = new BitTiming(BaudRate: baud);
            return (TSelf)this;
        }

        public virtual TSelf Fd(uint abit, uint dbit)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanFd);
            Options.BitTiming = new BitTiming(ArbitrationBitRate: abit, DataBitRate: dbit);
            return (TSelf)this;
        }

        public virtual TSelf BusUsage(uint periodMs = 1000)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.BusUsage);
            Options.BusUsageEnabled = true;
            Options.BusUsagePeriodTime = periodMs;
            return (TSelf)this;
        }

        public virtual TSelf SetTxRetryPolicy(TxRetryPolicy retryPolicy)
        {
            Options.TxRetryPolicy = retryPolicy;
            return (TSelf)this;
        }

        public virtual TSelf SetWorkMode(ChannelWorkMode mode)
        {
            Options.WorkMode = mode;
            return (TSelf)this;
        }

        public virtual TSelf InternalRes(bool enabled)
        {
            Options.InternalResistance = enabled;
            return (TSelf)this;
        }

        public virtual TSelf SetProtocolMode(CanProtocolMode mode)
        {
            Options.ProtocolMode = mode;
            Options.BitTiming = mode switch
            {
                CanProtocolMode.Can20 => new BitTiming(),
                _ => new BitTiming(null, 500_000, 500_000)
            };
            return (TSelf)this;
        }

        public virtual TSelf SetFilter(CanFilter filter)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.Filters);

            Options.Filter = filter;
            return (TSelf)this;
        }

        public virtual TSelf RangeFilter(uint min, uint max, CanFilterIDType idType = CanFilterIDType.Standard)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.Filters);
            if (min > max)
            {
                throw new ArgumentException($"Invalid range: min ({min}) must be less than or equal to max ({max}).",
                    nameof(max));
            }
            Options.Filter.filterRules.Add(new FilterRule.Range(min, max, idType));
            return (TSelf)this;

        }

        public virtual TSelf AccMask(uint accCode, uint accMask, CanFilterIDType idType = CanFilterIDType.Standard)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.Filters);
            Options.Filter.filterRules.Add(new FilterRule.Mask(accCode, accMask, idType));
            return (TSelf)this;
        }

        public virtual TSelf EnableErrorInfo()
        {
            Options.AllowErrorInfo = true;
            return (TSelf)this;
        }

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.Baud(uint baud)
            => Baud(baud);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.Fd(uint abit, uint dbit)
            => Fd(abit, dbit);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.BusUsage(uint periodMs)
            => BusUsage(periodMs);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.SetTxRetryPolicy(TxRetryPolicy retryPolicy)
            => SetTxRetryPolicy(retryPolicy);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.SetWorkMode(ChannelWorkMode mode)
            => SetWorkMode(mode);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.InternalRes(bool enabled)
            => InternalRes(enabled);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.SetProtocolMode(CanProtocolMode mode)
            => SetProtocolMode(mode);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.SetFilter(CanFilter filter)
            => SetFilter(filter);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.RangeFilter(uint min, uint max, CanFilterIDType idType)
            => RangeFilter(min, max, idType);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.AccMask(uint accCode, uint accMask, CanFilterIDType idType)
            => AccMask(accCode, accMask, idType);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.EnableErrorInfo()
            => EnableErrorInfo();
    }

}
