using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions;

public interface IDeviceRTOptionsConfigurator
{
    DeviceType DeviceType { get; }
    uint TxTimeOut { get; }
    bool MergeReceive { get; }
}


public interface IChannelRTOptionsConfigurator
{
    int ChannelIndex { get; }
    BitTiming BitTiming { get; }
    TxRetryPolicy TxRetryPolicy { get; }
    bool BusUsageEnabled { get; }
    uint BusUsagePeriodTime { get; }
    ChannelWorkMode WorkMode { get; }
    bool InternalResistance { get; } 
}

public interface IDeviceInitOptionsConfigurator
{
    DeviceType DeviceType { get; }
    uint TxTimeOutTime { get; }
    bool EnableMergeReceive { get; }
    
    IDeviceInitOptionsConfigurator TxTimeOut(uint ms);
    IDeviceInitOptionsConfigurator MergeReceive(bool enable);
}

public interface IChannelInitOptionsConfigurator
{
    int ChannelIndex { get; }
    BitTiming BitTiming { get; }
    TxRetryPolicy TxRetryPolicy { get; }
    bool BusUsageEnabled { get; }
    uint BusUsagePeriodTime { get; }
    ChannelWorkMode WorkMode { get; }
    bool InternalResistance { get; }
    
    IChannelInitOptionsConfigurator Baud(uint baud);
    IChannelInitOptionsConfigurator Fd(uint abit, uint dbit);
    IChannelInitOptionsConfigurator BusUsage(uint periodMs = 1000);
    IChannelInitOptionsConfigurator SetTxRetryPolicy(TxRetryPolicy retryPolicy);
    IChannelInitOptionsConfigurator SetWorkMode(ChannelWorkMode mode);
    IChannelInitOptionsConfigurator InternalRes(bool enabled);
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