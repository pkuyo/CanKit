using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Kvaser;

public sealed class KvaserBusOptions(ICanModelProvider provider) : IBusOptions
{
    public ICanModelProvider Provider { get; } = provider;

    public int ChannelIndex { get; set; }
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

    // Kvaser specific: preferred channel selector
    public int? ChannelNumber { get; set; }
    public string? ChannelName { get; set; }
    public bool AcceptVirtual { get; set; } = true;

    public void Apply(ICanApplier applier, bool force = false) => applier.Apply(this);
}

public sealed class KvaserBusInitConfigurator
    : BusInitOptionsConfigurator<KvaserBusOptions, KvaserBusInitConfigurator>
{
    public KvaserBusInitConfigurator UseChannelNumber(int index)
    {
        Options.ChannelNumber = index;
        return this;
    }

    public KvaserBusInitConfigurator UseChannelName(string name)
    {
        Options.ChannelName = name;
        return this;
    }

    public KvaserBusInitConfigurator AcceptVirtualChannels(bool enable = true)
    {
        Options.AcceptVirtual = enable;
        return this;
    }
}

public sealed class KvaserBusRtConfigurator
    : BusRtOptionsConfigurator<KvaserBusOptions>
{
    public int? ChannelNumber => Options.ChannelNumber;
    public string? ChannelName => Options.ChannelName;
    public bool AcceptVirtual => Options.AcceptVirtual;
}
