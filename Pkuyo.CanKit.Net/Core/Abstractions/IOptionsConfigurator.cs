using System;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions;


/// <summary>
/// Base interface for device/channel option configurators (设备/通道选项配置器的基础接口)。
/// </summary>
public interface ICanOptionsConfigurator
{
    /// <summary>
    /// Model provider for current options (当前设备/通道所属的模型提供者)。
    /// </summary>
    ICanModelProvider Provider { get; }


    /// <summary>
    /// Features supported by current options (当前设备/通道选项支持的功能)。
    /// </summary>
    CanFeature Features { get; }
}

/// <summary>
/// Runtime device option accessor (设备运行时选项访问器)。
/// </summary>
public interface IDeviceRTOptionsConfigurator : ICanOptionsConfigurator
{

    /// <summary>
    /// Device type (设备类型)。
    /// </summary>
    DeviceType DeviceType { get; }

    /// <summary>
    /// TX timeout in ms (发送超时，毫秒)。
    /// </summary>
    uint TxTimeOut { get; }
}


/// <summary>
/// Runtime channel option accessor (通道运行时选项访问器)。
/// </summary>
public interface IBusRTOptionsConfigurator : ICanOptionsConfigurator
{
    /// <summary>
    /// Channel index (通道索引)。
    /// </summary>
    int ChannelIndex { get; }

    /// <summary>
    /// Bit timing (位时序)。
    /// </summary>
    BitTiming BitTiming { get; }

    /// <summary>
    /// TX retry policy (发送重试策略)。
    /// </summary>
    TxRetryPolicy TxRetryPolicy { get; }

    /// <summary>
    /// Bus usage enabled (是否启用总线占用率统计)。
    /// </summary>
    bool BusUsageEnabled { get; }

    /// <summary>
    /// Bus usage period in ms (占用率统计周期，毫秒)。
    /// </summary>
    uint BusUsagePeriodTime { get; }

    /// <summary>
    /// Work mode (工作模式)。
    /// </summary>
    ChannelWorkMode WorkMode { get; }

    /// <summary>
    /// Internal termination enabled (是否启用内部终端电阻)。
    /// </summary>
    bool InternalResistance { get; }

    /// <summary>
    /// Protocol mode (协议模式)。
    /// </summary>
    CanProtocolMode ProtocolMode { get; }

    /// <summary>
    /// Active filter (当前过滤器)。
    /// </summary>
    ICanFilter Filter { get; }

    /// <summary>
    /// Software fallback enabled (是否启用软件替代)
    /// </summary>
    CanFeature EnabledSoftwareFallbackE { get; set; }

    /// <summary>
    /// Enable error information monitoring  (启用错误信息监听)。
    /// </summary>
    bool AllowErrorInfo { get; }
}

/// <summary>
/// Configurator for initializing device options (设备初始化选项配置器)。
/// </summary>
public interface IDeviceInitOptionsConfigurator : ICanOptionsConfigurator
{
    /// <summary>
    /// Device type (设备类型)。
    /// </summary>
    DeviceType DeviceType { get; }

    /// <summary>
    /// Default TX timeout in ms (发送超时默认值，毫秒)。
    /// </summary>
    uint TxTimeOutTime { get; }

    /// <summary>
    /// Set TX timeout (设置发送超时)。
    /// </summary>
    /// <param name="ms">Timeout in ms (超时毫秒)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IDeviceInitOptionsConfigurator TxTimeOut(uint ms);
}

/// <summary>
/// Configurator for initializing channel options (通道初始化选项配置器)。
/// </summary>
public interface IBusInitOptionsConfigurator : ICanOptionsConfigurator
{

    /// <summary>
    /// Channel index (通道索引)。
    /// </summary>
    int ChannelIndex { get; }

    /// <summary>
    /// Bit timing (位时序)。
    /// </summary>
    BitTiming BitTiming { get; }

    /// <summary>
    /// TX retry policy (发送重试策略)。
    /// </summary>
    TxRetryPolicy TxRetryPolicy { get; }

    /// <summary>
    /// Bus usage enabled (是否启用总线占用率统计)。
    /// </summary>
    bool BusUsageEnabled { get; }

    /// <summary>
    /// Bus usage period in ms (占用率统计周期，毫秒)。
    /// </summary>
    uint BusUsagePeriodTime { get; }

    /// <summary>
    /// Work mode (工作模式)。
    /// </summary>
    ChannelWorkMode WorkMode { get; }

    /// <summary>
    /// Internal termination enabled (是否启用内部终端电阻)。
    /// </summary>
    bool InternalResistance { get; }

    /// <summary>
    /// Protocol mode (协议模式)。
    /// </summary>
    CanProtocolMode ProtocolMode { get; }

    /// <summary>
    /// Current filter (当前过滤器)。
    /// </summary>
    ICanFilter Filter { get; }

    /// <summary>
    /// Software fallback enabled (是否启用软件替代)
    /// </summary>
    CanFeature EnabledSoftwareFallback { get; }

    /// <summary>
    /// Enable error information monitoring  (启用错误信息监听)。
    /// </summary>
    bool AllowErrorInfo { get; }

    /// <summary>
    /// Set channel bitrate (设置通道波特率)。
    /// </summary>
    /// <param name="baud">Bitrate in bps (比特率)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator Baud(uint baud);

    /// <summary>
    /// Set CAN FD arbitration/data bitrates (设置 CAN FD 仲裁/数据位率)。
    /// </summary>
    /// <param name="abit">Arbitration bitrate (仲裁位率)。</param>
    /// <param name="dbit">Data bitrate (数据位率)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator Fd(uint abit, uint dbit);

    /// <summary>
    /// Enable bus usage measurement (启用总线占用率统计)。
    /// </summary>
    /// <param name="periodMs">Period in ms (统计周期毫秒)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator BusUsage(uint periodMs = 1000);

    /// <summary>
    /// Enable/disable internal termination (启用/禁用内部终端电阻)。
    /// </summary>
    /// <param name="enabled">True to enable (是否启用)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator InternalRes(bool enabled);

    /// <summary>
    /// Set TX retry policy (设置发送重试策略)。
    /// </summary>
    /// <param name="retryPolicy">Retry policy (重试策略)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator SetTxRetryPolicy(TxRetryPolicy retryPolicy);

    /// <summary>
    /// Set channel work mode (设置通道工作模式)。
    /// </summary>
    /// <param name="mode">Work mode (工作模式)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator SetWorkMode(ChannelWorkMode mode);

    /// <summary>
    /// Set protocol mode (设置协议模式)。
    /// </summary>
    /// <param name="mode">Protocol mode (协议模式)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator SetProtocolMode(CanProtocolMode mode);

    /// <summary>
    /// Set filter (设置过滤器)。
    /// </summary>
    /// <param name="filter">Filter (过滤器)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator SetFilter(CanFilter filter);


    //TODO:
    IBusInitOptionsConfigurator SoftwareFeaturesFallBack(CanFeature features);

    /// <summary>
    /// Configure range filter by ID (按 ID 范围设置过滤器)。
    /// </summary>
    /// <param name="min">Min ID (最小 ID)。</param>
    /// <param name="max">Max ID (最大 ID)。</param>
    /// <param name="idType">ID type (ID 类型)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator RangeFilter(uint min, uint max, CanFilterIDType idType);

    /// <summary>
    /// Configure filter by acc-code/mask (通过验收码/屏蔽码设置过滤器)。
    /// </summary>
    /// <param name="accCode">Acceptance code (验收码)。</param>
    /// <param name="accMask">Acceptance mask (屏蔽码)。</param>
    /// <param name="idType">ID type (ID 类型)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator AccMask(uint accCode, uint accMask, CanFilterIDType idType);

    /// <summary>
    /// Enable error information monitoring (启用错误信息监听)。
    /// </summary>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator EnableErrorInfo();

}





public abstract class CallOptionsConfigurator<TOption, TSelf>
    where TOption : class, ICanOptions
    where TSelf : CallOptionsConfigurator<TOption, TSelf>
{
    protected TOption? _options;
    protected CanFeature _feature;
    private CanFeature _softwareFeature;
    public virtual TSelf Init(TOption options)
    {
        _options = options;
        // Initialize with static features; dynamic features can be merged later.
        _feature = options.Provider.StaticFeatures;
        return (TSelf)this;
    }

    protected TOption Options => _options ?? throw new InvalidOperationException("Options have not been initialized.");


    /// <summary>
    /// Merge dynamic features discovered at runtime into current capability set.
    /// ZH: 合并运行期发现的动态功能到当前能力集合。
    /// </summary>
    public TSelf UpdateDynamicFeatures(CanFeature dynamic)
    {
        _feature |= dynamic;
        return (TSelf)this;
    }


    /// <summary>
    /// Merge software fallback features discovered at runtime into current capability set.
    /// ZH: 合并软件替代功能到当前能力集合。
    /// </summary>
    public TSelf UpdateSoftwareFeatures(CanFeature softwareFeatures)
    {
        _feature &= ~_softwareFeature;
        _softwareFeature = softwareFeatures;
        _feature |= softwareFeatures;
        return (TSelf)this;
    }
    protected TSelf Self => (TSelf)this;
}
