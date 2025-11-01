using System.Collections.Generic;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Providers;
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
    public ICanFilter Filter { get; set; } = new CanFilter();
    public CanFeature EnabledSoftwareFallback { get; set; }
    public CanFeature Features { get; set; } = provider.StaticFeatures;
    public Capability Capabilities { get; set; } = new(provider.StaticFeatures, new Dictionary<string, object?>());
    public bool AllowErrorInfo { get; set; }
    public bool InternalResistance { get; set; }
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.AlwaysRetry;
    public int AsyncBufferCapacity { get; set; } = 0;
    public IBufferAllocator BufferAllocator { get; set; } = new DefaultBufferAllocator();
    public int PollingInterval { get; set; } = 10;
}
