using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;

namespace Pkuyo.CanKit.Net.SocketCAN;



public sealed class SocketCanBusOptions(ICanModelProvider provider) : IBusOptions
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
    public bool SoftwareFilterEnabled { get; set; }
    public bool AllowErrorInfo { get; set; }

    // SocketCAN specific: interface name, e.g. "can0", "vcan0" etc.
    public string InterfaceName { get; set; } = "can0";

    // Prefer kernel-provided timestamps (hardware if available, fallback to software)
    public bool PreferKernelTimestamp { get; set; } = true;
    public void Apply(ICanApplier applier, bool force = false) => applier.Apply(this);
}


public sealed class SocketCanBusInitConfigurator
    : BusInitOptionsConfigurator<SocketCanBusOptions, SocketCanBusInitConfigurator>
{
    public SocketCanBusInitConfigurator UseInterface(string name)
    {
        Options.InterfaceName = name;
        return this;
    }

    public SocketCanBusInitConfigurator PreferKernelTimestamp(bool enable = true)
    {
        Options.PreferKernelTimestamp = enable;
        return this;
    }

    public override SocketCanBusInitConfigurator RangeFilter(uint min, uint max, CanFilterIDType idType = CanFilterIDType.Standard)
    {
        if (!SoftwareFilterEnabled)
        {
            throw new CanFilterConfigurationException("SocketCAN only supports mask filters.");
        }
        return base.RangeFilter(min, max, idType);
    }
}

public sealed class SocketCanBusRtConfigurator
    : BusRtOptionsConfigurator<SocketCanBusOptions>
{
    public string InterfaceName => Options.InterfaceName;
    public bool PreferKernelTimestamp => Options.PreferKernelTimestamp;
}
