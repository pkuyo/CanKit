using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Virtual;

public sealed class VirtualBusOptions(ICanModelProvider provider) : IBusOptions
{

    public int ChannelIndex { get; set; }
    public string? ChannelName { get; set; }
    public CanBusTiming BitTiming { get; set; } = CanBusTiming.ClassicDefault();
    public bool InternalResistance { get; set; }
    public bool BusUsageEnabled { get; set; }
    public uint BusUsagePeriodTime { get; set; } = 1000U;
    public ChannelWorkMode WorkMode { get; set; } = ChannelWorkMode.Normal;
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.AlwaysRetry;
    public CanProtocolMode ProtocolMode { get; set; } = CanProtocolMode.Can20;
    public CanFilter Filter { get; set; } = new();
    public CanFeature EnabledSoftwareFallback { get; set; } = CanFeature.RangeFilter | CanFeature.MaskFilter | CanFeature.CyclicTx;
    public Capability Capabilities { get; set; } = new(provider.StaticFeatures);
    public bool AllowErrorInfo { get; set; }
    public int AsyncBufferCapacity { get; set; } = 0;
    public int ReceiveLoopStopDelayMs { get; set; } = 200;
    public IBufferAllocator BufferAllocator { get; set; } = new DefaultBufferAllocator();

    // Virtual specific: session id to join a hub
    public string SessionId { get; set; } = "default";
    public CanFeature Features { get; set; } = provider.StaticFeatures;

}

public sealed class VirtualBusInitConfigurator
    : BusInitOptionsConfigurator<VirtualBusOptions, VirtualBusInitConfigurator>
{
    public VirtualBusInitConfigurator UseSession(string sessionId)
    {
        Options.SessionId = sessionId;
        return this;
    }
}

public sealed class VirtualBusRtConfigurator
    : BusRtOptionsConfigurator<VirtualBusOptions, VirtualBusRtConfigurator>
{
    public string SessionId => Options.SessionId;
}

