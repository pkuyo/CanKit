using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;

namespace CanKit.Adapter.Candlelight;

public sealed class CandlelightDeviceOptions(ICanModelProvider provider) : IDeviceOptions
{
    public uint DeviceIndex { get; set; }
    public DeviceType DeviceType { get; } = provider.DeviceType;
    public CanFeature Features { get; set; } = provider.StaticFeatures;
}

public sealed class CandlelightDeviceInitConfigurator : DeviceInitOptionsConfigurator<CandlelightDeviceOptions, CandlelightDeviceInitConfigurator>
{
    public CandlelightDeviceInitConfigurator DeviceIndex(uint index)
    {
        Options.DeviceIndex = index;
        return this;
    }
}

public sealed class CandlelightDeviceRtConfigurator : DeviceRTOptionsConfigurator<CandlelightDeviceOptions>
{
    public uint DeviceIndex => Options.DeviceIndex;
}

public sealed class CandlelightBusOptions(ICanModelProvider provider) : IBusOptions
{
    public int ChannelIndex { get; set; }
    public string? ChannelName { get; set; }
    public CanBusTiming BitTiming { get; set; } = CanBusTiming.ClassicDefault();
    public bool InternalResistance { get; set; }
    public ChannelWorkMode WorkMode { get; set; } = ChannelWorkMode.Normal;
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.AlwaysRetry;
    public CanProtocolMode ProtocolMode { get; set; } = CanProtocolMode.Can20;
    public ICanFilter Filter { get; set; } = new CanFilter();
    public CanFeature EnabledSoftwareFallback { get; set; } = CanFeature.MaskFilter | CanFeature.RangeFilter | CanFeature.CyclicTx;
    public Capability Capabilities { get; set; } = new(provider.StaticFeatures);
    public CanFeature Features { get; set; } = provider.StaticFeatures;
    public bool AllowErrorInfo { get; set; }
    public int AsyncBufferCapacity { get; set; }
    public IBufferAllocator BufferAllocator { get; set; } = new DefaultBufferAllocator();
    public CanExceptionPolicy? ExceptionPolicy { get; set; }
}

public sealed class CandlelightBusInitConfigurator : BusInitOptionsConfigurator<CandlelightBusOptions, CandlelightBusInitConfigurator>;

public sealed class CandlelightBusRtConfigurator : BusRtOptionsConfigurator<CandlelightBusOptions, CandlelightBusRtConfigurator>;
