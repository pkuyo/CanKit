using System;
using System.Linq;
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
        public CanFeature Features => _feature;
        public DeviceType DeviceType => Options.DeviceType;
    }



    public class BusRtOptionsConfigurator<TChannelOptions, TSelf>
      : CallOptionsConfigurator<TChannelOptions, BusRtOptionsConfigurator<TChannelOptions, TSelf>>,
        IBusRTOptionsConfigurator
      where TChannelOptions : class, IBusOptions
      where TSelf : BusRtOptionsConfigurator<TChannelOptions, TSelf>
    {
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
        public int AsyncBufferCapacity => Options.AsyncBufferCapacity;

        public IBufferAllocator BufferAllocator => Options.BufferAllocator;

    }

    public class DeviceInitOptionsConfigurator<TDeviceOptions, TSelf>
        : CallOptionsConfigurator<TDeviceOptions, DeviceInitOptionsConfigurator<TDeviceOptions, TSelf>>,
            IDeviceInitOptionsConfigurator
        where TDeviceOptions : class, IDeviceOptions
        where TSelf : DeviceInitOptionsConfigurator<TDeviceOptions, TSelf>
    {
        public CanFeature Features => _feature;
        public DeviceType DeviceType => Options.DeviceType;
        public virtual IDeviceInitOptionsConfigurator Custom(string key, object value) => this;

    }

    public class BusInitOptionsConfigurator<TChannelOptions, TSelf>
      : CallOptionsConfigurator<TChannelOptions, BusInitOptionsConfigurator<TChannelOptions, TSelf>>,
        IBusInitOptionsConfigurator
      where TChannelOptions : class, IBusOptions
      where TSelf : BusInitOptionsConfigurator<TChannelOptions, TSelf>
    {
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
        public int AsyncBufferCapacity => Options.AsyncBufferCapacity;
        public int ReceiveLoopStopDelayMs => Options.ReceiveLoopStopDelayMs;


        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.Baud(int baud, uint? clockMHz, ushort? samplePointPermille)
            => Baud(baud, samplePointPermille);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.Fd(int abit, int dbit,
            uint? clockMHz,
            ushort? nominalSamplePointPermille,
            ushort? dataSamplePointPermille)
            => Fd(abit, dbit, nominalSamplePointPermille, dataSamplePointPermille);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.TimingClassic(CanClassicTiming timing)
            => TimingClassic(timing);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.TimingFd(CanFdTiming timing)
            => TimingFd(timing);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.BusUsage(int periodMs)
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


        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.RangeFilter(int min, int max, CanFilterIDType idType)
            => RangeFilter(min, max, idType);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.AccMask(int accCode, int accMask, CanFilterIDType idType)
            => AccMask(accCode, accMask, idType);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.AccMask(uint accCode, uint accMask, CanFilterIDType idType)
            => AccMask(accCode, accMask, idType);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.EnableErrorInfo()
            => EnableErrorInfo();

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.UseChannelIndex(int index)
            => UseChannelIndex(index);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.UseChannelName(string name)
            => UseChannelName(name);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.SetAsyncBufferCapacity(int capacity)
            => SetAsyncBufferCapacity(capacity);

        IBusInitOptionsConfigurator IBusInitOptionsConfigurator.BufferAllocator(IBufferAllocator bufferAllocator)
            => BufferAllocator(bufferAllocator);

        public virtual IBusInitOptionsConfigurator Custom(string key, object value) => this;

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


        public virtual TSelf Baud(int baud,
            uint? clockMHz = null,
            ushort? samplePointPermille = null)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanClassic);
            if (baud < 0) throw new ArgumentOutOfRangeException(nameof(baud));
            Options.BitTiming = new CanBusTiming(
                new CanClassicTiming(CanPhaseTiming.Target((uint)baud, samplePointPermille), clockMHz));
            return (TSelf)this;
        }

        public virtual TSelf Fd(int abit, int dbit, uint? clockMHz = null,
            ushort? nominalSamplePointPermille = null,
            ushort? dataSamplePointPermille = null)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanFd);
            if (abit < 0) throw new ArgumentOutOfRangeException(nameof(abit));
            if (dbit < 0) throw new ArgumentOutOfRangeException(nameof(dbit));
            Options.BitTiming = new CanBusTiming(
                new CanFdTiming(CanPhaseTiming.Target((uint)abit, nominalSamplePointPermille),
                    CanPhaseTiming.Target((uint)dbit, dataSamplePointPermille), clockMHz));
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

        public virtual TSelf BusUsage(int periodMs = 1000)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.BusUsage);
            if (periodMs < 0) throw new ArgumentOutOfRangeException(nameof(periodMs));
            Options.BusUsageEnabled = true;
            Options.BusUsagePeriodTime = (uint)periodMs;
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
            switch (mode)
            {
                case CanProtocolMode.Can20:
                    CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanClassic);
                    if (Options.BitTiming.Classic is null)
                        Options.BitTiming = CanBusTiming.ClassicDefault();
                    break;
                case CanProtocolMode.CanFd:
                    CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanFd);
                    if (Options.BitTiming.Fd is null)
                        Options.BitTiming = CanBusTiming.FdDefault();
                    break;
            }
            return (TSelf)this;
        }

        public virtual TSelf SetFilter(CanFilter filter)
        {
            var enableMask = ((_feature & CanFeature.MaskFilter) != 0) |
                             ((EnabledSoftwareFallback & CanFeature.MaskFilter) != 0);
            var enableRange = ((_feature & CanFeature.MaskFilter) != 0) |
                             ((EnabledSoftwareFallback & CanFeature.MaskFilter) != 0);
            if (enableRange && enableMask)
            { }
            else if (!enableMask && filter.FilterRules.Any(i => i is FilterRule.Mask))
                CanKitErr.ThrowIfNotSupport(_feature, CanFeature.MaskFilter);
            else if (!enableRange && filter.FilterRules.Any(i => i is FilterRule.Range))
                CanKitErr.ThrowIfNotSupport(_feature, CanFeature.RangeFilter);

            Options.Filter = filter;
            return (TSelf)this;
        }

        public virtual TSelf SoftwareFeaturesFallBack(CanFeature features)
        {
            Options.EnabledSoftwareFallback = features;
            UpdateSoftwareFeatures(features);
            return (TSelf)this;
        }

        public virtual TSelf RangeFilter(int min, int max, CanFilterIDType idType = CanFilterIDType.Standard)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.RangeFilter);
            if (min < 0) throw new ArgumentOutOfRangeException(nameof(min));
            if (max < 0) throw new ArgumentOutOfRangeException(nameof(max));
            if (min > max)
            {
                throw new ArgumentException($"Invalid range: min ({min}) must be less than or equal to max ({max}).",
                    nameof(max));
            }
            Options.Filter.filterRules.Add(new FilterRule.Range((uint)min, (uint)max, idType));
            return (TSelf)this;

        }

        public virtual TSelf AccMask(int accCode, int accMask, CanFilterIDType idType = CanFilterIDType.Standard)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.MaskFilter);
            // For AccMask, do not throw on negative; allow patterns like -1 (0xFFFFFFFF)
            Options.Filter.filterRules.Add(new FilterRule.Mask((uint)accCode, (uint)accMask, idType));
            return (TSelf)this;
        }
        public virtual TSelf AccMask(uint accCode, uint accMask, CanFilterIDType idType = CanFilterIDType.Standard)
        {
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.MaskFilter);
            Options.Filter.filterRules.Add(new FilterRule.Mask(accCode, accMask, idType));
            return (TSelf)this;
        }
        public virtual TSelf EnableErrorInfo()
        {
            Options.AllowErrorInfo = true;
            return (TSelf)this;
        }

        public virtual TSelf SetAsyncBufferCapacity(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            Options.AsyncBufferCapacity = capacity;
            return (TSelf)this;
        }
        public virtual TSelf SetReceiveLoopStopDelayMs(int milliseconds)
        {
            if (milliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(milliseconds));
            Options.ReceiveLoopStopDelayMs = milliseconds;
            return (TSelf)this;
        }

        public virtual TSelf BufferAllocator(IBufferAllocator bufferAllocator)
        {
            Options.BufferAllocator = bufferAllocator;
            return (TSelf)this;
        }
    }

}
