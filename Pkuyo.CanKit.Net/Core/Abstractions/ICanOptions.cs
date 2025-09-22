using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{

    /// <summary>
    /// Applies options to a target (用于将选项应用到目标的接口)。
    /// </summary>
    public interface ICanApplier
    {


        /// <summary>
        /// Apply a batch of options (批量应用选项)。
        /// </summary>
        /// <param name="options">Options to apply (要应用的选项对象)。</param>
        void Apply(ICanOptions options);

        /// <summary>
        /// Applier capability/type (应用器当前状态/类型)。
        /// </summary>
        CanOptionType ApplierStatus { get; }
    }


    /// <summary>
    /// Named applier that supports applying options by name (支持按名称应用选项的应用器)。
    /// </summary>
    public interface INamedCanApplier : ICanApplier
    {
        /// <summary>
        /// Apply a single option value (应用单个选项)。
        /// </summary>
        /// <typeparam name="T">Value type (值类型)。</typeparam>
        /// <param name="ID">Option name (选项名)。</param>
        /// <param name="value">Option value (选项值)。</param>
        /// <returns>True if applied (应用成功返回 true)。</returns>
        bool ApplyOne<T>(object ID, T value);
    }

    /// <summary>
    /// Unified options to be applied by an applier (统一的可应用选项对象)。
    /// </summary>
    public interface ICanOptions
    {
        /// <summary>
        /// Model provider owning these options (选项对应的模型提供者)。
        /// </summary>
        ICanModelProvider Provider { get; }

        /// <summary>
        /// Apply via the specified applier (通过给定应用器应用)。
        /// </summary>
        /// <param name="applier">Applier to read/commit options (用于读写选项的应用器)。</param>
        /// <param name="force">Force apply even if type not fully matched (是否强制应用)。</param>
        void Apply(ICanApplier applier, bool force = false);
    }



    /// <summary>
    /// Options related to CAN device (与 CAN 设备相关的选项)。
    /// </summary>
    public interface IDeviceOptions : ICanOptions
    {
        /// <summary>
        /// Device type (设备类型)。
        /// </summary>
        DeviceType DeviceType { get; }

        /// <summary>
        /// TX timeout in milliseconds (发送超时时间，毫秒)。
        /// </summary>
        uint TxTimeOut { get; set; }
    }

    /// <summary>
    /// Options related to CAN bus (与 CAN 总线相关的选项)。
    /// </summary>
    public interface IBusOptions : ICanOptions
    {

        /// <summary>
        /// Channel index (通道索引)。
        /// </summary>
        int ChannelIndex { get; set; }

        /// <summary>
        /// Bit timing (位时序)。
        /// </summary>
        BitTiming BitTiming { get; set; }

        /// <summary>
        /// Whether internal termination is enabled (是否启用内部终端电阻)。
        /// </summary>
        bool InternalResistance { get; set; }

        /// <summary>
        /// Whether bus usage measurement is enabled (是否启用总线占用率统计)。
        /// </summary>
        bool BusUsageEnabled { get; set; }

        /// <summary>
        /// Bus usage measurement period in ms (占用率统计周期，毫秒)。
        /// </summary>
        uint BusUsagePeriodTime { get; set; }

        /// <summary>
        /// Channel work mode (通道工作模式)。
        /// </summary>
        ChannelWorkMode WorkMode { get; set; }

        /// <summary>
        /// TX retry policy (发送重试策略)。
        /// </summary>
        TxRetryPolicy TxRetryPolicy { get; set; }

        /// <summary>
        /// CAN protocol mode (CAN 协议模式)。
        /// </summary>
        CanProtocolMode ProtocolMode { get; set; }

        /// <summary>
        /// ID/data filter (过滤器)。
        /// </summary>
        CanFilter Filter { get; set; }

    }
}

