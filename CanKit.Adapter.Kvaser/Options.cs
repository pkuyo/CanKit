using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Kvaser;

public sealed class KvaserBusOptions(ICanModelProvider provider) : IBusOptions
{
    // Kvaser specific
    public bool AcceptVirtual { get; set; } = true;
    public ICanModelProvider Provider { get; } = provider;

    public int ChannelIndex { get; set; }
    public string? ChannelName { get; set; }
    public CanBusTiming BitTiming { get; set; } = CanBusTiming.ClassicDefault();
    public bool InternalResistance { get; set; }
    public bool BusUsageEnabled { get; set; }
    public uint BusUsagePeriodTime { get; set; } = 1000U;
    public ChannelWorkMode WorkMode { get; set; } = ChannelWorkMode.Normal;
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.NoRetry;
    public CanProtocolMode ProtocolMode { get; set; } = CanProtocolMode.Can20;
    public CanFilter Filter { get; set; } = new();
    public CanFeature EnabledSoftwareFallback { get; set; }
    public bool AllowErrorInfo { get; set; }

    public void Apply(ICanApplier applier, bool force = false) => applier.Apply(this);
}

public sealed class KvaserBusInitConfigurator
    : BusInitOptionsConfigurator<KvaserBusOptions, KvaserBusInitConfigurator>
{
    public KvaserBusInitConfigurator AcceptVirtualChannels(bool enable = true)
    {
        Options.AcceptVirtual = enable;
        return this;
    }
}

public sealed class KvaserBusRtConfigurator
    : BusRtOptionsConfigurator<KvaserBusOptions>
{
    public bool AcceptVirtual => Options.AcceptVirtual;
}
