using System.Collections.Generic;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ControlCAN.Options;

public sealed class ControlCanDeviceOptions(ICanModelProvider provider) : IDeviceOptions
{
    public uint DeviceIndex { get; set; }
    public DeviceType DeviceType { get; } = provider.DeviceType;
    public CanFeature Features { get; set; } = provider.StaticFeatures;
}

public sealed class ControlCanBusOptions(ICanModelProvider provider) : IBusOptions
{
    public int ChannelIndex { get; set; }
    public string? ChannelName { get; set; }
    public CanBusTiming BitTiming { get; set; }
    public ChannelWorkMode WorkMode { get; set; }
    public CanProtocolMode ProtocolMode { get; set; }
    public CanFilter Filter { get; set; } = new();
    public CanFeature EnabledSoftwareFallback { get; set; }
    public CanFeature Features { get; set; } = provider.StaticFeatures;
    public Capability Capabilities { get; set; } = new(provider.StaticFeatures, new Dictionary<string, object?>());
    public bool AllowErrorInfo { get; set; }
    public bool InternalResistance { get; set; }
    public bool BusUsageEnabled { get; set; }
    public uint BusUsagePeriodTime { get; set; } = 200;
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.AlwaysRetry;
    public int AsyncBufferCapacity { get; set; } = 0;
    public int ReceiveLoopStopDelayMs { get; set; } = 200;
    public int PollingInterval { get; set; } = 10;
}
