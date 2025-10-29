using CanKit.Core.Definitions;

namespace CanKit.Core.Abstractions
{

    /// <summary>
    /// Unified options to be applied by an applier (统一的可应用选项对象)。
    /// </summary>
    public interface ICanOptions
    {
        /// <summary>
        /// Features supported by current options (当前设备/通道选项支持的功能)。
        /// </summary>
        CanFeature Features { get; set; }
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

        string? ChannelName { get; set; }

        /// <summary>
        /// Bit timing (位时序)。
        /// </summary>
        CanBusTiming BitTiming { get; set; }

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

        /// <summary>
        /// Enable software fallback for unsupported hardware features (启用软件替代功能)
        /// </summary>
        CanFeature EnabledSoftwareFallback { get; set; }

        /// Capability report combining built-in CanFeature and optional custom feature bag.
        /// (能力报告，包含内置的 CanFeature 与可选的自定义能力键值对。)
        Capability Capabilities { get; set; }

        /// <summary>
        /// Enable error information monitoring  (启用错误信息监听)。
        /// </summary>
        bool AllowErrorInfo { get; set; }


        /// <summary>
        /// Capacity of the internal async receive buffer (异步接收缓冲区容量)。
        /// </summary>
        int AsyncBufferCapacity { get; set; }

        /// <summary>
        /// Milliseconds to wait after a stop request before terminating the receive loop (接收循环停止延迟，毫秒)。
        /// </summary>
        int ReceiveLoopStopDelayMs { get; set; }

        /// <summary>
        /// Buffer allocator used to create frame payloads. （用于创建帧数据缓冲区的分配器。）
        /// </summary>
        IBufferAllocator BufferAllocator { get; set; }
    }
}
