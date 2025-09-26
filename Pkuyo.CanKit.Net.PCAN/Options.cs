using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.PCAN;

public sealed class PcanBusOptions(ICanModelProvider provider) : IBusOptions
{
    public ICanModelProvider Provider { get; } = provider;

    public int ChannelIndex { get; set; }
    public BitTiming BitTiming { get; set; } = new(500_000, null, null);
    public bool InternalResistance { get; set; }
    public bool BusUsageEnabled { get; set; }
    public uint BusUsagePeriodTime { get; set; } = 1000U;
    public ChannelWorkMode WorkMode { get; set; } = ChannelWorkMode.Normal;
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.NoRetry;
    public CanProtocolMode ProtocolMode { get; set; } = CanProtocolMode.Can20;
    public CanFilter Filter { get; set; } = new();
    public CanFeature EnabledSoftwareFallback { get; set; }
    public bool SoftwareFilterEnabled { get; set; }
    public bool AllowErrorInfo { get; set; }

    // PCAN specific: channel name, e.g. "PCAN_USBBUS1", "PCAN_PCIBUS1", etc.
    public string Channel { get; set; } = "PCAN_USBBUS1";

    public void Apply(ICanApplier applier, bool force = false) => applier.Apply(this);
}

public sealed class PcanBusInitConfigurator
    : BusInitOptionsConfigurator<PcanBusOptions, PcanBusInitConfigurator>
{
    public PcanBusInitConfigurator UseChannel(string channel)
    {
        Options.Channel = channel;
        return this;
    }
}

public sealed class PcanBusRtConfigurator
    : BusRtOptionsConfigurator<PcanBusOptions>
{
    public string Channel => Options.Channel;
}
