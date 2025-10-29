using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.Vector;

public sealed class VectorBusOptions(ICanModelProvider provider) : IBusOptions
{
    public int ChannelIndex { get; set; }
    public string? ChannelName { get; set; } = "CanKit";
    public CanBusTiming BitTiming { get; set; } = CanBusTiming.ClassicDefault();
    public ChannelWorkMode WorkMode { get; set; } = ChannelWorkMode.Normal;
    public CanProtocolMode ProtocolMode { get; set; } = CanProtocolMode.Can20;
    public CanFilter Filter { get; set; } = new();
    public CanFeature EnabledSoftwareFallback { get; set; }
    public Capability Capabilities { get; set; } = new(provider.StaticFeatures);
    public CanFeature Features { get; set; } = provider.StaticFeatures;
    public bool AllowErrorInfo { get; set; }
    public bool InternalResistance { get; set; }
    public bool BusUsageEnabled { get; set; }
    public uint BusUsagePeriodTime { get; set; } = 500;
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.AlwaysRetry;
    public int AsyncBufferCapacity { get; set; } = 0;
    public int ReceiveLoopStopDelayMs { get; set; } = 200;
    public IBufferAllocator BufferAllocator { get; set; } = new DefaultBufferAllocator();

    /// <summary>
    /// Polling interval in ms (only linux) (轮询间隔，毫秒，仅在linux下使用)。
    /// </summary>
    public int PollingInterval { get; set; } = 5;

    /// <summary>
    /// receive buffer capacity (in byte). (接收缓冲区，按字节为单位)
    /// </summary>
    public uint ReceiveBufferCapacity { get; set; } = 4096;
}

public sealed class VectorBusInitConfigurator
    : BusInitOptionsConfigurator<VectorBusOptions, VectorBusInitConfigurator>
{
    /// <summary>
    /// Set polling interval (only in linux) (设置轮询间隔，仅在linux下有效)。
    /// </summary>
    /// <param name="newPollingInterval">Interval in ms (间隔毫秒)。</param>
    public VectorBusInitConfigurator PollingInterval(int newPollingInterval)
    {
        if (newPollingInterval < 0)
            throw new ArgumentOutOfRangeException(nameof(newPollingInterval));
        Options.PollingInterval = newPollingInterval;
        return this;
    }

    public VectorBusInitConfigurator AppName(string appName)
    {
        Options.ChannelName = appName;
        return this;
    }


    /// <summary>
    /// Set receive buffer capacity (in byte). (设置接收缓冲区，按字节为单位)。
    /// </summary>
    /// <param name="receiveBufferCapacity">Interval in ms (间隔毫秒)。</param>
    public VectorBusInitConfigurator ReceiveBufferCapacity(int receiveBufferCapacity)
    {
        if (receiveBufferCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(receiveBufferCapacity));

        Options.ReceiveBufferCapacity = (uint)receiveBufferCapacity;
        return this;
    }

    public override VectorBusInitConfigurator RangeFilter(int min, int max,
        CanFilterIDType idType = CanFilterIDType.Standard)
    {
        if (idType == CanFilterIDType.Extend)
        {
            if ((Options.EnabledSoftwareFallback & CanFeature.RangeFilter) != 0)
                _options!.Filter.softwareFilter.Add(new FilterRule.Range((uint)min, (uint)max, idType));
            else
                throw new CanKitException(CanKitErrorCode.FeatureNotSupported, "Vector Bus only supports standard ID range filter");
            return this;
        }
        return base.RangeFilter(min, max, idType);
    }

    public override IBusInitOptionsConfigurator Custom(string key, object value)
    {
        switch (key)
        {
            case nameof(Options.PollingInterval):
                Options.PollingInterval = Convert.ToInt32(value);
                break;
            case nameof(Options.ReceiveBufferCapacity):
                Options.ReceiveBufferCapacity = Convert.ToUInt32(value);
                break;
            case nameof(AppName):
                Options.ChannelName = value.ToString();
                break;
            default:
                CanKitLogger.LogWarning($"Vector: invalid key: {key}");
                break;
        }
        return this;
    }
}

public sealed class VectorBusRtConfigurator
    : BusRtOptionsConfigurator<VectorBusOptions, VectorBusRtConfigurator>
{

    /// <summary>
    /// Polling interval in ms (轮询间隔，毫秒)。
    /// </summary>
    public int PollingInterval
    {
        get => Options.PollingInterval;
        set => Options.PollingInterval = value;
    }

    /// <summary>
    /// receive buffer capacity (in byte). (接收缓冲区，按字节为单位)
    /// </summary>
    public uint ReceiveBufferCapacity => Options.ReceiveBufferCapacity;
}









