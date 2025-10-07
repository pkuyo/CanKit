using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.SocketCAN;



public sealed class SocketCanBusOptions(ICanModelProvider provider) : IBusOptions
{
    public ICanModelProvider Provider { get; } = provider;

    public int ChannelIndex { get; set; }
    public string? ChannelName { get; set; } = "can0";
    public CanBusTiming BitTiming { get; set; } = CanBusTiming.ClassicDefault();
    public bool InternalResistance { get; set; }
    public bool BusUsageEnabled { get; set; }
    public uint BusUsagePeriodTime { get; set; } = 1000U;
    public ChannelWorkMode WorkMode { get; set; } = ChannelWorkMode.Normal;
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.AlwaysRetry;
    public CanProtocolMode ProtocolMode { get; set; } = CanProtocolMode.Can20;
    public CanFilter Filter { get; set; } = new();
    public CanFeature EnabledSoftwareFallback { get; set; }
    public bool AllowErrorInfo { get; set; }
    public int AsyncBufferCapacity { get; set; } = 0;
    public int ReceiveLoopStopDelayMs { get; set; } = 200;

    /// <summary>
    /// Prefer kernel-provided timestamps (hardware if available, fallback to software)
    /// </summary>
    public bool PreferKernelTimestamp { get; set; } = true;

    /// <summary>
    /// When true, configure the CAN interface via libsocketcan.
    /// Requires root or CAP_NET_ADMIN privileges;
    /// </summary>

    public bool UseNetLink { get; set; }

    /// <summary>
    /// Read timeout, in milliseconds, for blocking receive operations on the CAN socket.
    /// </summary>
    public int ReadTImeOutMs { get; set; } = 5;
}


public sealed class SocketCanBusInitConfigurator
    : BusInitOptionsConfigurator<SocketCanBusOptions, SocketCanBusInitConfigurator>
{
    public SocketCanBusInitConfigurator PreferKernelTimestamp(bool enable = true)
    {
        Options.PreferKernelTimestamp = enable;
        return this;
    }

    public override SocketCanBusInitConfigurator RangeFilter(uint min, uint max, CanFilterIDType idType = CanFilterIDType.Standard)
    {
        if ((Options.EnabledSoftwareFallback & CanFeature.Filters) == 0)
        {
            throw new CanFilterConfigurationException("SocketCAN only supports mask filters.");
        }
        return base.RangeFilter(min, max, idType);
    }

    public SocketCanBusInitConfigurator NetLink(bool enable)
    {
        Options.UseNetLink = enable;
        return this;
    }

    public SocketCanBusInitConfigurator ReadTImeOut(int ms)
    {
        Options.ReadTImeOutMs = ms;
        return this;
    }

    public int ReadTImeOutMs => Options.ReadTImeOutMs;
    public bool UseNetLink => Options.UseNetLink;
}

public sealed class SocketCanBusRtConfigurator
    : BusRtOptionsConfigurator<SocketCanBusOptions, SocketCanBusRtConfigurator>
{
    public bool PreferKernelTimestamp => Options.PreferKernelTimestamp;

    public bool UseNetLink => Options.UseNetLink;

    public int ReadTImeOutMs => Options.ReadTImeOutMs;
}
