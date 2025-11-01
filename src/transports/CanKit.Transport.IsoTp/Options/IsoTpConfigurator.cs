using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Core.Definitions;
using CanKit.Core.Utils;

namespace CanKit.Protocol.IsoTp.Options;

public class IsoTpInitConfigurator
    : CallOptionsConfigurator<IsoTpOptions, IsoTpInitConfigurator>, IBusInitOptionsConfigurator
{
    public int ChannelIndex => Options.ChannelIndex;
    public string? ChannelName => Options.ChannelName;
    public CanBusTiming BitTiming => Options.BitTiming;

    public bool InternalResistance => Options.InternalResistance;
    public CanProtocolMode ProtocolMode => Options.ProtocolMode;


    public int AsyncBufferCapacity => Options.AsyncBufferCapacity;

    public virtual IsoTpInitConfigurator CanPadding(bool padding)
    {
        Options.CanPadding = padding;
        return this;
    }

    public virtual IsoTpInitConfigurator GlobalBusGuard(TimeSpan globalBusGuard)
    {
        Options.GlobalBusGuard = globalBusGuard;
        return this;
    }

    public virtual IsoTpInitConfigurator MaxFrameLength(int maxFrameLength)
    {
        Options.MaxFrameLength = maxFrameLength;
        return this;
    }

    public virtual IsoTpInitConfigurator N_As(TimeSpan n_As)
    {
        Options.N_As = n_As;
        return this;
    }

    public virtual IsoTpInitConfigurator N_Ar(TimeSpan n_Ar)
    {
        Options.N_Ar = n_Ar;
        return this;
    }

    public virtual IsoTpInitConfigurator N_Bs(TimeSpan n_Bs)
    {
        Options.N_Bs = n_Bs;
        return this;
    }

    public virtual IsoTpInitConfigurator N_Br(TimeSpan n_Br)
    {
        Options.N_Br = n_Br;
        return this;
    }

    public virtual IsoTpInitConfigurator N_Cs(TimeSpan n_Cs)
    {
        Options.N_Cs = n_Cs;
        return this;
    }

    public virtual IsoTpInitConfigurator N_Cr(TimeSpan n_Cr)
    {
        Options.N_Cr = n_Cr;
        return this;
    }

    public virtual IBusInitOptionsConfigurator Custom(string key, object value) => this;

    public virtual IsoTpInitConfigurator UseChannelIndex(int index)
    {
        Options.ChannelIndex = index;
        return this;
    }

    public virtual IsoTpInitConfigurator UseChannelName(string name)
    {
        Options.ChannelName = name;
        return this;
    }


    public virtual IsoTpInitConfigurator Baud(int baud,
        uint? clockMHz = null,
        ushort? samplePointPermille = null)
    {
        CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanClassic);
        if (baud < 0) throw new ArgumentOutOfRangeException(nameof(baud));
        Options.BitTiming = new CanBusTiming(
            new CanClassicTiming(CanPhaseTiming.Target((uint)baud, samplePointPermille), clockMHz));
        return this;
    }

    public virtual IsoTpInitConfigurator Fd(int abit, int dbit, uint? clockMHz = null,
        ushort? nominalSamplePointPermille = null,
        ushort? dataSamplePointPermille = null)
    {
        CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanFd);
        if (abit < 0) throw new ArgumentOutOfRangeException(nameof(abit));
        if (dbit < 0) throw new ArgumentOutOfRangeException(nameof(dbit));
        Options.BitTiming = new CanBusTiming(
            new CanFdTiming(CanPhaseTiming.Target((uint)abit, nominalSamplePointPermille),
                CanPhaseTiming.Target((uint)dbit, dataSamplePointPermille), clockMHz));
        return this;
    }


    public virtual IsoTpInitConfigurator TimingClassic(CanClassicTiming timing)
    {
        CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanClassic);
        Options.BitTiming = new CanBusTiming(timing);
        return this;
    }


    public virtual IsoTpInitConfigurator TimingFd(CanFdTiming timing)
    {
        CanKitErr.ThrowIfNotSupport(_feature, CanFeature.CanFd);
        Options.BitTiming = new CanBusTiming(timing);
        return this;
    }

    public virtual IsoTpInitConfigurator InternalRes(bool enabled)
    {
        Options.InternalResistance = enabled;
        return this;
    }

    public virtual IsoTpInitConfigurator SetProtocolMode(CanProtocolMode mode)
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

        return this;
    }

    public virtual IsoTpInitConfigurator SetAsyncBufferCapacity(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Options.AsyncBufferCapacity = capacity;
        return this;
    }


    public virtual IsoTpInitConfigurator BufferAllocator(IBufferAllocator bufferAllocator)
    {
        Options.BufferAllocator = bufferAllocator;
        return this;
    }

    #region Ignored

    TxRetryPolicy IBusInitOptionsConfigurator.TxRetryPolicy => Options.TxRetryPolicy;

    ChannelWorkMode IBusInitOptionsConfigurator.WorkMode => Options.WorkMode;

    ICanFilter IBusInitOptionsConfigurator.Filter => Options.Filter;

    CanFeature IBusInitOptionsConfigurator.EnabledSoftwareFallback => Options.EnabledSoftwareFallback;

    bool IBusInitOptionsConfigurator.AllowErrorInfo => Options.AllowErrorInfo;
    CanFeature ICanOptionsConfigurator.Features => Options.Features;

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

    IBusInitOptionsConfigurator IBusInitOptionsConfigurator.SetTxRetryPolicy(TxRetryPolicy retryPolicy)
        => SetTxRetryPolicy(retryPolicy);

    IBusInitOptionsConfigurator IBusInitOptionsConfigurator.SetWorkMode(ChannelWorkMode mode)
        => SetWorkMode(mode);

    IBusInitOptionsConfigurator IBusInitOptionsConfigurator.InternalRes(bool enabled)
        => InternalRes(enabled);

    IBusInitOptionsConfigurator IBusInitOptionsConfigurator.SetProtocolMode(CanProtocolMode mode)
        => SetProtocolMode(mode);

    IBusInitOptionsConfigurator IBusInitOptionsConfigurator.SetFilter(ICanFilter filter)
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

    protected virtual IsoTpInitConfigurator SetTxRetryPolicy(TxRetryPolicy retryPolicy)
    {
        Options.TxRetryPolicy = retryPolicy;
        return this;
    }

    protected virtual IsoTpInitConfigurator SetWorkMode(ChannelWorkMode mode)
    {
        Options.WorkMode = mode;
        return this;
    }

    protected virtual IsoTpInitConfigurator SetFilter(ICanFilter filter)
    {
        var enableMask = ((_feature & CanFeature.MaskFilter) != 0) |
                         ((Options.EnabledSoftwareFallback & CanFeature.MaskFilter) != 0);
        var enableRange = ((_feature & CanFeature.MaskFilter) != 0) |
                          ((Options.EnabledSoftwareFallback & CanFeature.MaskFilter) != 0);
        if (enableRange && enableMask)
        {
        }
        else if (!enableMask && filter.FilterRules.Any(i => i is FilterRule.Mask))
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.MaskFilter);
        else if (!enableRange && filter.FilterRules.Any(i => i is FilterRule.Range))
            CanKitErr.ThrowIfNotSupport(_feature, CanFeature.RangeFilter);

        Options.Filter = filter;
        return this;
    }

    protected virtual IsoTpInitConfigurator SoftwareFeaturesFallBack(CanFeature features)
    {
        Options.EnabledSoftwareFallback = features;
        UpdateSoftwareFeatures(features);
        return this;
    }

    protected virtual IsoTpInitConfigurator RangeFilter(int min, int max,
        CanFilterIDType idType = CanFilterIDType.Standard)
    {
        CanKitErr.ThrowIfNotSupport(_feature, CanFeature.RangeFilter);
        if (min < 0) throw new ArgumentOutOfRangeException(nameof(min));
        if (max < 0) throw new ArgumentOutOfRangeException(nameof(max));
        if (min > max)
        {
            throw new ArgumentException($"Invalid range: min ({min}) must be less than or equal to max ({max}).",
                nameof(max));
        }

        Options.Filter.FilterRules.Add(new FilterRule.Range((uint)min, (uint)max, idType));
        return this;
    }

    protected virtual IsoTpInitConfigurator AccMask(int accCode, int accMask,
        CanFilterIDType idType = CanFilterIDType.Standard)
    {
        CanKitErr.ThrowIfNotSupport(_feature, CanFeature.MaskFilter);
        // For AccMask, do not throw on negative; allow patterns like -1 (0xFFFFFFFF)
        Options.Filter.FilterRules.Add(new FilterRule.Mask((uint)accCode, (uint)accMask, idType));
        return this;
    }

    protected virtual IsoTpInitConfigurator AccMask(uint accCode, uint accMask,
        CanFilterIDType idType = CanFilterIDType.Standard)
    {
        CanKitErr.ThrowIfNotSupport(_feature, CanFeature.MaskFilter);
        Options.Filter.FilterRules.Add(new FilterRule.Mask(accCode, accMask, idType));
        return this;
    }

    protected virtual IsoTpInitConfigurator EnableErrorInfo()
    {
        Options.AllowErrorInfo = true;
        return this;
    }
    #endregion

}

public class IsoTpRtConfigurator : CallOptionsConfigurator<IsoTpOptions, IsoTpRtConfigurator>, IBusRTOptionsConfigurator
{
    public bool CanPadding => Options.CanPadding;
    public TimeSpan? GlobalBusGuard => Options.GlobalBusGuard;
    public bool N_AxCheck => Options.N_AxCheck;
    public QueuedCanBusOptions? QueuedCanBusOptions => Options.QueuedCanBusOptions;

    public TimeSpan N_As => Options.N_As;
    public TimeSpan N_Ar => Options.N_Ar;
    public TimeSpan N_Bs => Options.N_Bs;
    public TimeSpan N_Br => Options.N_Br;
    public TimeSpan N_Cs => Options.N_Cs;
    public TimeSpan N_Cr => Options.N_Cr;

    public int MaxFrameLength => Options.MaxFrameLength;
    public int ChannelIndex => Options.ChannelIndex;
    public string? ChannelName => Options.ChannelName;
    public CanBusTiming BitTiming => Options.BitTiming;



    public bool InternalResistance => Options.InternalResistance;
    public CanProtocolMode ProtocolMode => Options.ProtocolMode;
    public int AsyncBufferCapacity => Options.AsyncBufferCapacity;
    public IBufferAllocator BufferAllocator => Options.BufferAllocator;


    #region Ignored

    TxRetryPolicy IBusRTOptionsConfigurator.TxRetryPolicy => Options.TxRetryPolicy;
    ChannelWorkMode IBusRTOptionsConfigurator.WorkMode => Options.WorkMode;
    ICanFilter IBusRTOptionsConfigurator.Filter => Options.Filter;
    CanFeature IBusRTOptionsConfigurator.EnabledSoftwareFallback => Options.EnabledSoftwareFallback;
    bool IBusRTOptionsConfigurator.AllowErrorInfo => Options.AllowErrorInfo;
    CanFeature ICanOptionsConfigurator.Features => Options.Features;

    #endregion

}
