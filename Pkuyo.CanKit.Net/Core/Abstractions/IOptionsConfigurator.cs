using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions;


public interface ICanOptionsConfigurator
{
    ICanModelProvider Provider { get; }
}

public interface IDeviceRTOptionsConfigurator : ICanOptionsConfigurator
{
    DeviceType DeviceType { get; }
    uint TxTimeOut { get; }
}


public interface IChannelRTOptionsConfigurator : ICanOptionsConfigurator
{
    int ChannelIndex { get; }
    BitTiming BitTiming { get; }
    TxRetryPolicy TxRetryPolicy { get; }
    bool BusUsageEnabled { get; }
    uint BusUsagePeriodTime { get; }
    ChannelWorkMode WorkMode { get; }
    bool InternalResistance { get; } 
    CanProtocolMode ProtocolMode { get; }
}

public interface IDeviceInitOptionsConfigurator : ICanOptionsConfigurator
{
    DeviceType DeviceType { get; }
    uint TxTimeOutTime { get; }
    
    IDeviceInitOptionsConfigurator TxTimeOut(uint ms);
}

public interface IChannelInitOptionsConfigurator : ICanOptionsConfigurator
{
    int ChannelIndex { get; }
    BitTiming BitTiming { get; }
    TxRetryPolicy TxRetryPolicy { get; }
    bool BusUsageEnabled { get; }
    uint BusUsagePeriodTime { get; }
    ChannelWorkMode WorkMode { get; }
    bool InternalResistance { get; }
    CanProtocolMode ProtocolMode { get; }
    
    IChannelInitOptionsConfigurator Baud(uint baud);
    IChannelInitOptionsConfigurator Fd(uint abit, uint dbit);
    IChannelInitOptionsConfigurator BusUsage(uint periodMs = 1000);
    IChannelInitOptionsConfigurator SetTxRetryPolicy(TxRetryPolicy retryPolicy);
    IChannelInitOptionsConfigurator SetWorkMode(ChannelWorkMode mode);
    IChannelInitOptionsConfigurator InternalRes(bool enabled);
    IChannelInitOptionsConfigurator SetProtocolMode(CanProtocolMode mode);
}





public abstract class CallOptionsConfigurator<TOption, TSelf>
    where TOption : class, ICanOptions
    where TSelf   : CallOptionsConfigurator<TOption, TSelf>
{
    protected TOption  _options;
    protected CanFeature _feature;

    public virtual TSelf Init(TOption options)
    {
        _options = options;
        _feature = options.Provider.Features;
        return (TSelf)this;
    }

    protected TOption Options  => _options;
    protected CanFeature Feature => _feature;
    protected TSelf Self => (TSelf)this;
}