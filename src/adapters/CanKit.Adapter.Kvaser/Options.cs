using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;

namespace CanKit.Adapter.Kvaser;

public sealed class KvaserBusOptions(ICanModelProvider provider) : IBusOptions
{
    // Kvaser specific
    public bool AcceptVirtual { get; set; } = true;
    public ICanModelProvider Provider { get; } = provider;
    /// <summary>
    /// Timer scale in microseconds per time unit returned by CANlib (kv timer_scale).
    /// Default: 1000 (milliseconds).
    /// </summary>
    public int TimerScaleMicroseconds { get; set; } = 1000;

    public int ChannelIndex { get; set; }
    public string? ChannelName { get; set; }
    public CanBusTiming BitTiming { get; set; } = CanBusTiming.ClassicDefault();
    public bool InternalResistance { get; set; }
    public ChannelWorkMode WorkMode { get; set; } = ChannelWorkMode.Normal;
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.AlwaysRetry;
    public CanProtocolMode ProtocolMode { get; set; } = CanProtocolMode.Can20;
    public ICanFilter Filter { get; set; } = new CanFilter();
    public CanFeature EnabledSoftwareFallback { get; set; }
    public Capability Capabilities { get; set; } = new(provider.StaticFeatures);
    public CanFeature Features { get; set; } = provider.StaticFeatures;
    public bool AllowErrorInfo { get; set; }
    public int AsyncBufferCapacity { get; set; } = 0;
    public IBufferAllocator BufferAllocator { get; set; } = new DefaultBufferAllocator();

    public int? ReceiveBufferCapacity { get; set; }

}

public sealed class KvaserBusInitConfigurator
    : BusInitOptionsConfigurator<KvaserBusOptions, KvaserBusInitConfigurator>
{
    public KvaserBusInitConfigurator AcceptVirtualChannels(bool enable = true)
    {
        Options.AcceptVirtual = enable;
        return this;
    }
    public KvaserBusInitConfigurator TimerScaleMicroseconds(int microseconds)
    {
        Options.TimerScaleMicroseconds = microseconds;
        return this;
    }

    public KvaserBusInitConfigurator ReceiveBufferCapacity(int? capacity)
    {
        if ((capacity ?? 0) < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Options.ReceiveBufferCapacity = capacity;
        return this;
    }

    public override IBusInitOptionsConfigurator Custom(string key, object value)
    {
        switch (key)
        {
            case nameof(Options.AcceptVirtual):
            case nameof(AcceptVirtualChannels):
                Options.AcceptVirtual = ThrowOrGet<bool>(value);
                break;
            case nameof(TimerScaleMicroseconds):
                Options.TimerScaleMicroseconds = Convert.ToInt32(value);
                break;
            case nameof(ReceiveBufferCapacity):
                Options.ReceiveBufferCapacity = Convert.ToInt32(value);
                break;
            default:
                CanKitLogger.LogWarning($"Kvaser: invalid key: {key}");
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
}

public sealed class KvaserBusRtConfigurator
    : BusRtOptionsConfigurator<KvaserBusOptions, KvaserBusRtConfigurator>
{
    public bool AcceptVirtual => Options.AcceptVirtual;
    public int TimerScaleMicroseconds => Options.TimerScaleMicroseconds;

    public int? ReceiveBufferCapacity => Options.ReceiveBufferCapacity;

}
