using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions;


/// <summary>
///     提供访问通用配置上下文的基础接口。
/// </summary>
public interface ICanOptionsConfigurator
{ 
    /// <summary>
    ///     获取创建当前配置器的模型提供程序。
    /// </summary>
    ICanModelProvider Provider { get; }
}

/// <summary>
///     定义在运行时获取设备相关参数的配置器。
/// </summary>
public interface IDeviceRTOptionsConfigurator : ICanOptionsConfigurator
{ 

    /// <summary>
    ///     获取设备的类型。
    /// </summary>
    DeviceType DeviceType { get; }

    /// <summary>
    ///     获取发送超时时间，单位为毫秒。
    /// </summary>
    uint TxTimeOut { get; }
}


/// <summary>
///     定义在运行时读取通道参数的配置器。
/// </summary>
public interface IChannelRTOptionsConfigurator : ICanOptionsConfigurator
{ 
    /// <summary>
    ///     获取通道索引。
    /// </summary>
    int ChannelIndex { get; }

    /// <summary>
    ///     获取通道的位时序设置。
    /// </summary>
    BitTiming BitTiming { get; }

    /// <summary>
    ///     获取发送重试策略。
    /// </summary>
    TxRetryPolicy TxRetryPolicy { get; }

    /// <summary>
    ///     获取是否启用总线负载监控。
    /// </summary>
    bool BusUsageEnabled { get; }

    /// <summary>
    ///     获取总线负载监控的周期，单位为毫秒。
    /// </summary>
    uint BusUsagePeriodTime { get; }

    /// <summary>
    ///     获取通道的工作模式。
    /// </summary>
    ChannelWorkMode WorkMode { get; }

    /// <summary>
    ///     获取是否启用了内部终端电阻。
    /// </summary>
    bool InternalResistance { get; }

    /// <summary>
    ///     获取 CAN 协议模式。
    /// </summary>
    CanProtocolMode ProtocolMode { get; }

    /// <summary>
    ///     获取当前使用的过滤器对象。
    /// </summary>
    ICanFilter Filter { get; }
}

/// <summary>
///     定义用于构建设备初始化配置的配置器。
/// </summary>
public interface IDeviceInitOptionsConfigurator : ICanOptionsConfigurator
{ 
    /// <summary>
    ///     获取设备类型。
    /// </summary>
    DeviceType DeviceType { get; }

    /// <summary>
    ///     获取发送超时的默认值，单位为毫秒。
    /// </summary>
    uint TxTimeOutTime { get; }

    /// <summary>
    ///     设置发送超时并返回自身以便链式调用。
    /// </summary>
    /// <param name="ms">超时时间，单位为毫秒。</param>
    /// <returns>当前配置器实例。</returns>
    IDeviceInitOptionsConfigurator TxTimeOut(uint ms);
}

/// <summary>
///     定义用于构建通道初始化配置的配置器。
/// </summary>
public interface IChannelInitOptionsConfigurator : ICanOptionsConfigurator
{ 

    /// <summary>
    ///     获取通道索引。
    /// </summary>
    int ChannelIndex { get; }

    /// <summary>
    ///     获取位时序设置。
    /// </summary>
    BitTiming BitTiming { get; }

    /// <summary>
    ///     获取发送重试策略。
    /// </summary>
    TxRetryPolicy TxRetryPolicy { get; }

    /// <summary>
    ///     获取是否启用总线负载监控。
    /// </summary>
    bool BusUsageEnabled { get; }

    /// <summary>
    ///     获取总线负载监控周期，单位为毫秒。
    /// </summary>
    uint BusUsagePeriodTime { get; }

    /// <summary>
    ///     获取工作模式。
    /// </summary>
    ChannelWorkMode WorkMode { get; }

    /// <summary>
    ///     获取是否启用内部终端电阻。
    /// </summary>
    bool InternalResistance { get; }

    /// <summary>
    ///     获取协议模式。
    /// </summary>
    CanProtocolMode ProtocolMode { get; }

    /// <summary>
    ///     获取过滤器对象。
    /// </summary>
    ICanFilter Filter { get; }

    /// <summary>
    ///     设置通道波特率。
    /// </summary>
    /// <param name="baud">目标波特率。</param>
    /// <returns>当前配置器实例。</returns>
    IChannelInitOptionsConfigurator Baud(uint baud);

    /// <summary>
    ///     设置 CAN FD 模式的仲裁段与数据段波特率。
    /// </summary>
    /// <param name="abit">仲裁段波特率。</param>
    /// <param name="dbit">数据段波特率。</param>
    /// <returns>当前配置器实例。</returns>
    IChannelInitOptionsConfigurator Fd(uint abit, uint dbit);

    /// <summary>
    ///     启用总线占用监控并设置周期。
    /// </summary>
    /// <param name="periodMs">监控周期，单位为毫秒。</param>
    /// <returns>当前配置器实例。</returns>
    IChannelInitOptionsConfigurator BusUsage(uint periodMs = 1000);

    /// <summary>
    ///     配置是否启用内部终端电阻。
    /// </summary>
    /// <param name="enabled">若为 <c>true</c> 则启用内部终端电阻。</param>
    /// <returns>当前配置器实例。</returns>
    IChannelInitOptionsConfigurator InternalRes(bool enabled);

    /// <summary>
    ///     设置发送重试策略。
    /// </summary>
    /// <param name="retryPolicy">重试策略。</param>
    /// <returns>当前配置器实例。</returns>
    IChannelInitOptionsConfigurator SetTxRetryPolicy(TxRetryPolicy retryPolicy);

    /// <summary>
    ///     设置通道工作模式。
    /// </summary>
    /// <param name="mode">目标工作模式。</param>
    /// <returns>当前配置器实例。</returns>
    IChannelInitOptionsConfigurator SetWorkMode(ChannelWorkMode mode);

    /// <summary>
    ///     设置协议模式。
    /// </summary>
    /// <param name="mode">目标协议模式。</param>
    /// <returns>当前配置器实例。</returns>
    IChannelInitOptionsConfigurator SetProtocolMode(CanProtocolMode mode);

    /// <summary>
    ///     设置过滤器。
    /// </summary>
    /// <param name="filter">过滤器参数。</param>
    /// <returns>当前配置器实例。</returns>
    IChannelInitOptionsConfigurator SetFilter(CanFilter filter);

    /// <summary>
    ///     按照 ID 范围配置过滤器。
    /// </summary>
    /// <param name="min">最小 ID。</param>
    /// <param name="max">最大 ID。</param>
    /// <param name="idType">过滤器的 ID 类型。</param>
    /// <returns>当前配置器实例。</returns>
    IChannelInitOptionsConfigurator RangeFilter(uint min, uint max, FilterIDType idType);

    /// <summary>
    ///     通过验收码与掩码配置过滤器。
    /// </summary>
    /// <param name="accCode">验收码。</param>
    /// <param name="accMask">验收掩码。</param>
    /// <param name="idType">过滤器的 ID 类型。</param>
    /// <returns>当前配置器实例。</returns>
    IChannelInitOptionsConfigurator AccMask(uint accCode, uint accMask, FilterIDType idType);
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
