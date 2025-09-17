using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{
    
    /// <summary>
    ///     定义将配置项应用到目标对象的行为。
    /// </summary>
    public interface ICanApplier
    {
        /// <summary>
        ///     尝试将单个配置项应用到目标对象。
        /// </summary>
        /// <typeparam name="T">配置值的类型。</typeparam>
        /// <param name="name">配置项名称。</param>
        /// <param name="value">配置项的目标值。</param>
        /// <returns>应用是否成功。</returns>
        bool ApplyOne<T>(string name, T value);

        /// <summary>
        ///     批量应用另一份配置对象中的内容。
        /// </summary>
        /// <param name="options">需要被应用的配置对象。</param>
        void Apply(ICanOptions options);

        /// <summary>
        ///     获取当前应用器的状态，用于指示配置项是否完全生效。
        /// </summary>
        CanOptionType ApplierStatus { get; }
    }

    /// <summary>
    ///     表示具有统一应用能力的配置对象。
    /// </summary>
    public interface ICanOptions
    {
        /// <summary>
        ///     获取创建当前配置的模型提供程序。
        /// </summary>
        ICanModelProvider Provider { get; }

        /// <summary>
        ///     使用给定的应用器对配置执行应用操作。
        /// </summary>
        /// <param name="applier">用于解释并写入配置项的应用器。</param>
        /// <param name="force">是否强制应用，即使目标应用器状态不完全匹配。</param>
        void Apply(ICanApplier applier, bool force = false);
    }



    /// <summary>
    ///     定义与 CAN 设备相关的配置。
    /// </summary>
    public interface IDeviceOptions : ICanOptions
    {
        /// <summary>
        ///     获取设备的类型。
        /// </summary>
        DeviceType DeviceType { get; }

        /// <summary>
        ///     获取或设置发送超时时间，单位为毫秒。
        /// </summary>
        uint TxTimeOut { get; set; }
    }

    /// <summary>
    ///     定义与 CAN 通道相关的配置。
    /// </summary>
    public interface IChannelOptions : ICanOptions
    {

        /// <summary>
        ///     获取或设置通道索引。
        /// </summary>
        int ChannelIndex { get; set; }

        /// <summary>
        ///     获取或设置通道使用的位时序参数。
        /// </summary>
        BitTiming BitTiming { get; set; }

        /// <summary>
        ///     获取或设置是否启用内部终端电阻。
        /// </summary>
        bool InternalResistance { get; set; }

        /// <summary>
        ///     获取或设置是否启用总线负载监控。
        /// </summary>
        bool BusUsageEnabled { get; set; }

        /// <summary>
        ///     获取或设置总线负载监控的周期，单位为毫秒。
        /// </summary>
        uint BusUsagePeriodTime { get; set; }

        /// <summary>
        ///     获取或设置通道的工作模式。
        /// </summary>
        ChannelWorkMode WorkMode { get; set; }

        /// <summary>
        ///     获取或设置发送重试策略。
        /// </summary>
        TxRetryPolicy TxRetryPolicy { get; set; }

        /// <summary>
        ///     获取或设置 CAN 协议模式（例如标准、FD 等）。
        /// </summary>
        CanProtocolMode ProtocolMode { get; set; }

        /// <summary>
        ///     获取或设置接收过滤器参数。
        /// </summary>
        CanFilter Filter { get; set; }
    }
}
