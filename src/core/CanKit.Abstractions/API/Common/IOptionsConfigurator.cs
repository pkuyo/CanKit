using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Core.Diagnostics;

namespace CanKit.Abstractions.API.Common;


/// <summary>
/// Base interface for device/channel option configurators (设备/通道选项配置器的基础接口)。
/// </summary>
public interface ICanOptionsConfigurator
{
    /// <summary>
    /// Features supported by current options (当前设备/通道选项支持的功能)。
    /// </summary>
    CanFeature Features { get; }

    /// Capability report combining built-in CanFeature and optional custom feature bag.
    /// (能力报告，包含内置的 CanFeature 与可选的自定义能力键值对。)
    Capability Capabilities { get; }
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
    /// Channel name (通道名称)。
    /// </summary>
    string? ChannelName { get; }

    /// <summary>
    /// Bit timing (位时序)。
    /// </summary>
    CanBusTiming BitTiming { get; }

    /// <summary>
    /// TX retry policy (发送重试策略)。
    /// </summary>
    TxRetryPolicy TxRetryPolicy { get; }

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
    CanFeature EnabledSoftwareFallback { get; }

    /// <summary>
    /// Enable error information monitoring  (启用错误信息监听)。
    /// </summary>
    bool AllowErrorInfo { get; }

    /// <summary>
    /// Capacity of the internal async receive buffer (异步接收缓冲区容量)。
    /// </summary>
    int AsyncBufferCapacity { get; }

    /// <summary>
    /// Buffer allocator used for frame payloads at runtime.
    /// 运行时用于帧数据缓冲区的分配器。
    /// </summary>
    IBufferAllocator BufferAllocator { get; }

    /// <summary>
    /// Optional exception handling policy for this bus instance. （CAN总线异常处理策略）
    /// When null, <see cref="CanExceptionPolicy.Default"/> is used. （null时使用CanExceptionPolicy.Default）
    /// </summary>
    CanExceptionPolicy? ExceptionPolicy { get; set; }
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
    /// Set custom option value (设置自定义选项值)。
    /// </summary>
    /// <param name="key">Option key (选项键)。</param>
    /// <param name="value">Option value (选项值)。</param>
    IDeviceInitOptionsConfigurator Custom(string key, object value);
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
    /// Channel name (通道名称)。
    /// </summary>
    string? ChannelName { get; }

    /// <summary>
    /// Bit timing (位时序)。
    /// </summary>
    CanBusTiming BitTiming { get; }

    /// <summary>
    /// TX retry policy (发送重试策略)。
    /// </summary>
    TxRetryPolicy TxRetryPolicy { get; }

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
    /// Capacity of the internal async receive buffer (异步接收缓冲区容量)。
    /// </summary>
    int AsyncBufferCapacity { get; }


    /// <summary>
    /// Set channel bitrate (设置通道波特率)。
    /// </summary>
    /// <param name="baud">Bitrate in bps (比特率)。</param>
    /// <param name="clockMHz">device clock frequency (设备时钟频率）。</param>
    /// <param name="samplePointPermille"></param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator Baud(int baud,
        uint? clockMHz = null,
        ushort? samplePointPermille = null);

    /// <summary>
    /// Set CAN FD arbitration/data bitrates (设置 CAN FD 仲裁/数据位率)。
    /// </summary>
    /// <param name="abit">Arbitration bitrate (仲裁位率)。</param>
    /// <param name="dbit">Data bitrate (数据位率)。</param>
    /// <param name="clockMHz">device clock frequency (设备时钟频率）。</param>
    /// <param name="nominalSamplePointPermille"></param>
    /// <param name="dataSamplePointPermille"></param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator Fd(int abit, int dbit,
        uint? clockMHz = null,
        ushort? nominalSamplePointPermille = null,
        ushort? dataSamplePointPermille = null);

    /// <summary>
    /// Set classic timing with a full timing config.
    /// ZH: 设置经典 CAN 位时序（完整配置）。
    /// </summary>
    IBusInitOptionsConfigurator TimingClassic(CanClassicTiming timing);


    /// <summary>
    /// Set CAN FD timing with a full timing config.
    /// ZH: 设置 CAN FD 位时序（完整配置）。
    /// </summary>
    IBusInitOptionsConfigurator TimingFd(CanFdTiming timing);

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
    IBusInitOptionsConfigurator SetFilter(ICanFilter filter);


    /// <summary>
    /// Enable software fallback for specified features (启用指定功能的软件替代)。
    /// </summary>
    /// <param name="features">Feature flags to enable in software (要启用的软件替代功能标志)。</param>
    IBusInitOptionsConfigurator SoftwareFeaturesFallBack(CanFeature features);

    /// <summary>
    /// Configure range filter by ID (按 ID 范围设置过滤器)。
    /// </summary>
    /// <param name="min">Min ID (最小 ID)。</param>
    /// <param name="max">Max ID (最大 ID)。</param>
    /// <param name="idType">ID type (ID 类型)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator RangeFilter(int min, int max, CanFilterIDType idType);

    /// <summary>
    /// Configure filter by acc-code/mask (通过验收码/屏蔽码设置过滤器)。
    /// </summary>
    /// <param name="accCode">Acceptance code (验收码)。</param>
    /// <param name="accMask">Acceptance mask (屏蔽码)。</param>
    /// <param name="idType">ID type (ID 类型)。</param>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator AccMask(int accCode, int accMask, CanFilterIDType idType);
    IBusInitOptionsConfigurator AccMask(uint accCode, uint accMask, CanFilterIDType idType);
    /// <summary>
    /// Enable error information monitoring (启用错误信息监听)。
    /// </summary>
    /// <returns>Configurator (配置器本身)。</returns>
    IBusInitOptionsConfigurator EnableErrorInfo();


    /// <summary>
    /// Select channel by index (通过索引选择通道)。
    /// </summary>
    /// <param name="index">Channel index (通道索引)。</param>
    IBusInitOptionsConfigurator UseChannelIndex(int index);


    /// <summary>
    /// Select channel by name (通过名称选择通道)。
    /// </summary>
    /// <param name="name">Channel name (通道名称)。</param>
    IBusInitOptionsConfigurator UseChannelName(string name);

    /// <summary>
    /// Set capacity for internal async receive buffer (设置异步接收缓冲区容量)。
    /// </summary>
    /// <param name="capacity">Buffer capacity in frames (缓冲区容量，单位：帧)。</param>
    IBusInitOptionsConfigurator SetAsyncBufferCapacity(int capacity);

    /// <summary>
    /// Set the buffer allocator for frame payloads.(设置用于帧负载的数据缓冲区分配器。)
    /// </summary>
    /// <param name="bufferAllocator">Allocator instance. (分配器实例)。</param>
    IBusInitOptionsConfigurator BufferAllocator(IBufferAllocator bufferAllocator);

    /// <summary>
    /// Optional exception handling policy for this bus instance. （CAN总线异常处理策略）
    /// </summary>
    IBusInitOptionsConfigurator ExceptionPolicy(CanExceptionPolicy exceptionPolicy);


    /// <summary>
    /// Set custom option value (设置自定义选项值)。
    /// </summary>
    /// <param name="key">Option key (选项键)。</param>
    /// <param name="value">Option value (选项值)。</param>
    IBusInitOptionsConfigurator Custom(string key, object value);
}





public abstract class CallOptionsConfigurator<TOption, TSelf>
    where TOption : class, ICanOptions
    where TSelf : CallOptionsConfigurator<TOption, TSelf>
{
    protected CanFeature _feature;
    protected TOption? _options;
    private CanFeature _softwareFeature;

    protected TOption Options => _options ?? throw new InvalidOperationException("Options have not been initialized.");

    public virtual TSelf Init(TOption options)
    {
        _options = options;
        _feature = options.Features;
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
}
