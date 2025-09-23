using System;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    /// <summary>
    /// Frame error kinds that may occur (帧可能发生的错误类型)
    /// </summary>
    [Flags]
    public enum FrameErrorKind : uint
    {
        None = 0,            // no error / 无错误
        BitError = 1,            // bit error / 位错误
        StuffError = 1 << 1,       // stuff error / 填充位错误
        CrcError = 1 << 2,       // crc error / CRC 校验错误
        FormError = 1 << 3,       // form error / 帧格式错误
        AckError = 1 << 4,       // missing ACK / 未收到 ACK
        Controller = 1 << 5,       // generic controller error / 控制器通用错误
        Warning = 1 << 6,       // error warning state / 错误报警
        Passive = 1 << 7,       // error passive state / 错误被动
        Overload = 1 << 8,       // bus overload / 总线过载
        RxOverflow = 1 << 9,       // receive overflow / 接收溢出
        TxOverflow = 1 << 10,      // transmit overflow / 发送溢出
        ArbitrationLost = 1 << 11,      // arbitration lost / 仲裁丢失
        BusError = 1 << 12,      // bus error / 总线错误
        BusOff = 1 << 13,      // bus off / 总线关闭
        TxTimeout = 1 << 14,      // transmit timeout / 发送超时
        Restarted = 1 << 15,      // controller restarted / 控制器重启
        TransceiverError = 1 << 16,      // transceiver error / 收发器错误
        DeviceError = 1 << 17,      // device error / 设备错误
        DriverError = 1 << 18,      // driver error / 驱动错误
        ResourceError = 1 << 19,      // resource exhausted / 资源不足
        CommandFailed = 1 << 20,      // command failed / 命令失败

        Unknown = 1 << 30

    }
    /// <summary>
    /// Frame direction: Tx or Rx (帧方向：发送或接收)
    /// </summary>
    public enum FrameDirection
    {
        Unknown = 0,
        Tx,
        Rx
    }



    /// <summary>
    /// Frame type flags used to identify frame kind (帧类型标志)
    /// </summary>
    [Flags]
    public enum CanFrameType : uint
    {
        Invalid = 0,
        Can20 = 1 << 0,
        CanFd = 1 << 1,
        Error = 1 << 2,
        Any = int.MaxValue
    }

    /// <summary>
    /// Supported CAN protocol modes (支持的协议模式)
    /// </summary>
    public enum CanProtocolMode
    {
        Can20 = 0,
        CanFd = 1,
        Merged = 2
    }

    /// <summary>
    /// Device capability flags (设备功能标志)
    /// </summary>
    [Flags]
    public enum CanFeature
    {
        CanClassic = 1 << 0,
        CanFd = 1 << 1,
        MergeReceive = 1 << 2,

        Filters = 1 << 3,

        CyclicTx = 1 << 4,
        BusUsage = 1 << 5,
        ErrorCounters = 1 << 6,
    }

    /// <summary>
    /// Options application phase (选项应用阶段)
    /// </summary>
    public enum CanOptionType
    {
        Init = 1,
        Runtime = 2
    }

    /// <summary>
    /// TX retry policies (发送重试策略)
    /// </summary>
    public enum TxRetryPolicy : byte
    {
        NoRetry = 1,
        AlwaysRetry = 2,
    }

    /// <summary>
    /// Channel work mode (通道工作模式)
    /// </summary>
    public enum ChannelWorkMode : byte
    {
        Normal = 0,
        ListenOnly = 1,
        Echo = 2,
    }

    /// <summary>
    /// Filter ID type: standard or extended (过滤 ID 类型：标/扩展)
    /// </summary>
    public enum CanFilterIDType
    {
        Standard,
        Extend,
    }

    /// <summary>
    /// CAN Bus state (CAN总线错误状态)
    /// </summary>
    public enum BusState
    {
        None = 0,
        ErrActive = 1,
        ErrWarning = 2,
        ErrPassive = 3,
        BusOff = 4,

        Unknown = int.MaxValue
    }
}


