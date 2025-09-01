using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions;

public interface IDeviceRTOptionsConfigurator<out T> where T : IDeviceOptions
{
    DeviceType DeviceType { get; }
    uint TxTimeOut { get; }
    bool MergeReceive { get; }
}


public interface IChannelRTOptionsConfigurator<out T> where T : IChannelOptions
{
    int ChannelIndex { get; }
    BitTiming BitTiming { get; }
    TxRetryPolicy TxRetryPolicy { get; }
    bool BusUsageEnabled { get; }
    uint BusUsagePeriodTime { get; }
    ChannelWorkMode WorkMode { get; }
    bool InternalResistance { get; } 
}

public interface IDeviceInitOptionsConfigurator<out T> where T : IDeviceOptions
{
    DeviceType DeviceType { get; }
    uint TxTimeOutTime { get; }
    bool EnableMergeReceive { get; }
}

public interface IChannelInitOptionsConfigurator<out T> where T : IChannelOptions
{
    int ChannelIndex { get; }
    BitTiming BitTiming { get; }
    TxRetryPolicy TxRetryPolicy { get; }
    bool BusUsageEnabled { get; }
    uint BusUsagePeriodTime { get; }
    ChannelWorkMode WorkMode { get; }
    bool InternalResistance { get; }
}

public interface IDeviceInitOptionsMutable<TSelf, T>
    where TSelf : IDeviceInitOptionsMutable<TSelf, T>
    where T : class, IDeviceOptions
{
    TSelf Init(T options, CanFeature feature);
    TSelf TxTimeOut(uint ms);
    TSelf MergeReceive(bool enable);
}

public interface IChannelInitOptionsMutable<TSelf, T>
    where TSelf : IChannelInitOptionsMutable<TSelf, T>
    where T : class, IChannelOptions
{
    TSelf Init(T options, CanFeature feature);
    TSelf Baud(uint baud);
    TSelf Fd(uint abit, uint dbit);
    TSelf BusUsage(uint periodMs = 1000);
    TSelf SetTxRetryPolicy(TxRetryPolicy retryPolicy);
    TSelf SetWorkMode(ChannelWorkMode mode);
    // 如需可写的终端电阻设置（仅作示例）
    TSelf InternalRes(bool enabled);
}

public abstract class CallOptionsConfigurator<TOption, TSelf>
    where TOption : class, ICanOptions
    where TSelf   : CallOptionsConfigurator<TOption, TSelf>
{
    protected TOption  _options;
    protected CanFeature _feature;

    public virtual TSelf Init(TOption options, CanFeature feature)
    {
        _options = options;
        _feature = feature;
        return (TSelf)this;
    }

    protected TOption Options  => _options;
    protected CanFeature Feature => _feature;
    protected TSelf Self => (TSelf)this;
}