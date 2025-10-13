using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
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
    public bool PreferKernelTimestamp { get; set; }

    /// <summary>
    /// Read timeout, in milliseconds, for blocking receive operations on the CAN socket.
    /// </summary>
    public int ReadTImeOutMs { get; set; } = 5;

    /// <summary>
    /// When true, configure the CAN interface via libsocketcan.
    /// Requires root or CAP_NET_ADMIN privileges;
    /// </summary>

    public bool UseNetLink { get; set; }


    /// <summary>
    /// esired per-socket receive buffer capacity (in bytes) for the  CAN socket.
    /// </summary>
    public uint? ReceiveBufferCapacity { get; set; }

    /// <summary>
    /// esired per-socket transmit buffer capacity (in bytes) for the  CAN socket.
    /// </summary>
    public uint? TransmitBufferCapacity { get; set; }
}


public sealed class SocketCanBusInitConfigurator
    : BusInitOptionsConfigurator<SocketCanBusOptions, SocketCanBusInitConfigurator>
{

    public override SocketCanBusInitConfigurator RangeFilter(int min, int max, CanFilterIDType idType = CanFilterIDType.Standard)
    {
        if ((Options.EnabledSoftwareFallback & CanFeature.Filters) == 0)
        {
            throw new CanFilterConfigurationException("SocketCAN only supports mask filters.");
        }
        return base.RangeFilter(min, max, idType);
    }

    public SocketCanBusInitConfigurator PreferKernelTimestamp(bool enable = true)
    {
        Options.PreferKernelTimestamp = enable;
        return this;
    }

    public SocketCanBusInitConfigurator NetLink(bool enable)
    {
        Options.UseNetLink = enable;
        return this;
    }

    public SocketCanBusInitConfigurator ReceiveBufferCapacity(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Options.ReceiveBufferCapacity = (uint)capacity;
        return this;
    }
    public SocketCanBusInitConfigurator TransmitBufferCapacity(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Options.TransmitBufferCapacity = (uint)capacity;
        return this;
    }
    public SocketCanBusInitConfigurator ReadTimeOut(int ms)
    {
        Options.ReadTImeOutMs = ms;
        return this;
    }

    public override IBusInitOptionsConfigurator Custom(string key, object value)
    {
        switch (key)
        {
            case nameof(PreferKernelTimestamp):
                Options.PreferKernelTimestamp = ThrowOrGet<bool>(value);
                break;
            case nameof(UseNetLink):
                Options.UseNetLink = ThrowOrGet<bool>(value);
                break;
            case nameof(ReadTimeOut):
            case nameof(ReadTImeOutMs):
                Options.ReadTImeOutMs = Convert.ToInt32(value);
                break;
            case nameof(ReceiveBufferCapacity):
                Options.ReceiveBufferCapacity = Convert.ToUInt32(value);
                break;
            case nameof(TransmitBufferCapacity):
                Options.TransmitBufferCapacity = Convert.ToUInt32(value);
                break;
            default:
                CanKitLogger.LogWarning($"SocketCAN: invalid key: {key}");
                break;
        }

        T ThrowOrGet<T>(object value)
        {
            if (value is not T t)
                throw new ArgumentException(nameof(value));
            return t;
        }
        return this;
    }

    public bool UseNetLink => Options.UseNetLink;

    public int ReadTImeOutMs => Options.ReadTImeOutMs;
}

public sealed class SocketCanBusRtConfigurator
    : BusRtOptionsConfigurator<SocketCanBusOptions, SocketCanBusRtConfigurator>
{
    public bool PreferKernelTimestamp => Options.PreferKernelTimestamp;

    public bool UseNetLink => Options.UseNetLink;

    public int ReadTImeOutMs => Options.ReadTImeOutMs;

    public uint? ReceiveBufferCapacity => Options.ReceiveBufferCapacity;

    public uint? TransmitBufferCapacity => Options.TransmitBufferCapacity;
}
