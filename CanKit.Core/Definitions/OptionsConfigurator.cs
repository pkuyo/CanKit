using System;
using CanKit.Core.Abstractions;
using CanKit.Core.Utils;

namespace CanKit.Core.Definitions
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
    }



    public class BusRtOptionsConfigurator<TChannelOptions>
      : CallOptionsConfigurator<TChannelOptions, BusRtOptionsConfigurator<TChannelOptions>>,
        IBusRTOptionsConfigurator
      where TChannelOptions : class, IBusOptions
    {
        public ICanModelProvider Provider => Options.Provider;
        public CanFeature Features => _feature;
        public int ChannelIndex => Options.ChannelIndex;
        public string? ChannelName => Options.ChannelName;
        public CanBusTiming BitTiming => Options.BitTiming;
        public TxRetryPolicy TxRetryPolicy => Options.TxRetryPolicy;
        public bool BusUsageEnabled => Options.BusUsageEnabled;
        public uint BusUsagePeriodTime => Options.BusUsagePeriodTime;
        public ChannelWorkMode WorkMode => Options.WorkMode;
        public bool InternalResistance => Options.InternalResistance;
        public CanProtocolMode ProtocolMode => Options.ProtocolMode;
        public ICanFilter Filter => Options.Filter;

        public CanFeature EnabledSoftwareFallback => Options.EnabledSoftwareFallback;
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
        public string? ChannelName => Options.ChannelName;
        public CanBusTiming BitTiming => Options.BitTiming;
        public TxRetryPolicy TxRetryPolicy => Options.TxRetryPolicy;
        public bool BusUsageEnabled => Options.BusUsageEnabled;
        public uint BusUsagePeriodTime => Options.BusUsagePeriodTime;
        public ChannelWorkMode WorkMode => Options.WorkMode;
        public bool InternalResistance => Options.InternalResistance;
        public CanProtocolMode ProtocolMode => Options.ProtocolMode;

        public ICanFilter Filter => Options.Filter;
        public CanFeature EnabledSoftwareFallback => Options.EnabledSoftwareFallback;

        public bool AllowErrorInfo => Options.AllowErrorInfo;

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.Baud(uint baud, uint? clockMHz, ushort? samplePointPermille)
            => Baud(baud, samplePointPermille);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.Fd(uint abit, uint dbit,
            uint? clockMHz,
            ushort? nominalSamplePointPermille,
            ushort? dataSamplePointPermille)
            => Fd(abit, dbit, nominalSamplePointPermille, dataSamplePointPermille);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.TimingClassic(CanClassicTiming timing)
            => TimingClassic(timing);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.TimingFd(CanFdTiming timing)
            => TimingFd(timing);

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

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.SoftwareFeaturesFallBack(CanFeature features)
            => SoftwareFeaturesFallBack(features);


        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.RangeFilter(uint min, uint max, CanFilterIDType idType)
            => RangeFilter(min, max, idType);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.AccMask(uint accCode, uint accMask, CanFilterIDType idType)
            => AccMask(accCode, accMask, idType);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.EnableErrorInfo()
            => EnableErrorInfo();

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.UseChannelIndex(int index)
            => UseChannelIndex(index);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.UseChannelName(string name)
            => UseChannelName(name);

        public virtual TSelf UseChannelIndex(int index)
        {
            Options.ChannelIndex = index;
            return (TSelf)this;
        }

        public virtual TSelf UseChannelName(string name)
        {
            Options.ChannelName = name;
            return (TSelf)this;
        }


        public virtual TSelf Baud(uint baud,
            uint? clockMHz = null,
            ushort? samplePointPermille = null)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanClassic);
            Options.BitTiming = new CanBusTiming(
                new CanClassicTiming(CanPhaseTiming.Target(baud, samplePointPermille), clockMHz));
            return (TSelf)this;
        }

        public virtual TSelf Fd(uint abit, uint dbit, uint? clockMHz = null,
            ushort? nominalSamplePointPermille = null,
            ushort? dataSamplePointPermille = null)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanFd);
            Options.BitTiming = new CanBusTiming(
                new CanFdTiming(CanPhaseTiming.Target(abit, nominalSamplePointPermille),
                    CanPhaseTiming.Target(dbit, dataSamplePointPermille), clockMHz));
            return (TSelf)this;
        }


        public virtual TSelf TimingClassic(CanClassicTiming timing)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanClassic);
            Options.BitTiming = new CanBusTiming(timing);
            return (TSelf)this;
        }


        public virtual TSelf TimingFd(CanFdTiming timing)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanFd);
            Options.BitTiming = new CanBusTiming(timing);
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
                CanProtocolMode.Can20 => CanBusTiming.ClassicDefault(),
                CanProtocolMode.CanFd => CanBusTiming.FdDefault(),
                _ => throw new Exception() //TODO:异常处理
            };
            return (TSelf)this;
        }

        public virtual TSelf SetFilter(CanFilter filter)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.Filters);

            Options.Filter = filter;
            return (TSelf)this;
        }

        public virtual TSelf SoftwareFeaturesFallBack(CanFeature features)
        {
            Options.EnabledSoftwareFallback = features;
            UpdateSoftwareFeatures(features);
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
    }

}
