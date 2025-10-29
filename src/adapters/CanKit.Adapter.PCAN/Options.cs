using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.PCAN;

public sealed class PcanBusOptions(ICanModelProvider provider) : IBusOptions
{
    public bool SoftwareFilterEnabled { get; set; }
    public ICanModelProvider Provider { get; } = provider;

    public int ChannelIndex { get; set; }
    public string? ChannelName { get; set; } = "PCAN_USBBUS1";
    public CanBusTiming BitTiming { get; set; } = CanBusTiming.ClassicDefault();
    public bool InternalResistance { get; set; }
    public bool BusUsageEnabled { get; set; }
    public uint BusUsagePeriodTime { get; set; } = 1000U;
    public ChannelWorkMode WorkMode { get; set; } = ChannelWorkMode.Normal;
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.AlwaysRetry;
    public CanProtocolMode ProtocolMode { get; set; } = CanProtocolMode.Can20;
    public CanFilter Filter { get; set; } = new();
    public CanFeature EnabledSoftwareFallback { get; set; }
    public Capability Capabilities { get; set; } = new Capability(provider.StaticFeatures);
    public bool AllowErrorInfo { get; set; }
    public int AsyncBufferCapacity { get; set; } = 0;
    public int ReceiveLoopStopDelayMs { get; set; } = 200;
    public IBufferAllocator BufferAllocator { get; set; } = new DefaultBufferAllocator();
    public CanFeature Features { get; set; } = provider.StaticFeatures;
}

public sealed class PcanBusInitConfigurator
    : BusInitOptionsConfigurator<PcanBusOptions, PcanBusInitConfigurator>;

public sealed class PcanBusRtConfigurator
    : BusRtOptionsConfigurator<PcanBusOptions, PcanBusRtConfigurator>;
