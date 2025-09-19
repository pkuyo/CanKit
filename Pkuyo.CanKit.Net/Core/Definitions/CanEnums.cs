using System;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    /// <summary>
    /// Frame error kinds that may occur (帧可能发生的错误类型)。
    /// </summary>
    public enum FrameErrorKind
    {
        None        = 0,         // no error / 无错误
        BitError    = 1,         // bit error / 位错误
        StuffError  = 2,         // stuff error / 填充位错误
        CrcError    = 3,         // crc error / CRC 校验错误
        FormError   = 4,         // form error / 帧格式错误
        AckError    = 5,         // missing ACK / 未收到 ACK
        Controller  = 6,
        
        Unknown     = 65535
    }

    /// <summary>
    /// Frame direction: Tx or Rx (帧方向：发送或接收)。
    /// </summary>
    public enum FrameDirection
    {
        Unknown = 0,
        Tx,
        Rx
    }

    /// <summary>
    /// Channel error state (通道错误状态)。
    /// </summary>
    public enum ChannelErrorState
    {
        None        = 0,
        AlarmErr    = 1,
        PassiveErr  = 2,
        BusOff      = 3,
        BusRestart  = 4,
    }
    
    /// <summary>
    /// Frame type flags used to identify frame kind (帧类型标志)。
    /// </summary>
    [Flags]
    public enum CanFrameType : uint
    {
        Invalid    = 0,
        Can20      = 1 << 0,
        CanFd      = 1 << 1,
        Error      = 1 << 2,
        Any        = int.MaxValue
    }

    /// <summary>
    /// Supported CAN protocol modes (支持的协议模式)。
    /// </summary>
    public enum CanProtocolMode
    {
        Can20 = 0,
        CanFd = 1,
        Merged = 2
    }

    /// <summary>
    /// Device capability flags (设备功能标志)。
    /// </summary>
    [Flags]
    public enum CanFeature
    {
        CanClassic          = 1 << 0,
        CanFd               = 1 << 1,
        MergeReceive        = 1 << 2,
        
        Filters             = 1 << 3,
        
        CyclicTx            = 1 << 4,
        BusUsage            = 1 << 5,
        ErrorCounters       = 1 << 6,
    }

    /// <summary>
    /// Options application phase (选项应用阶段)。
    /// </summary>
    public enum CanOptionType
    {
        Init = 1,
        Runtime = 2
    }

    /// <summary>
    /// TX retry policies (发送重试策略)。
    /// </summary>
    public enum TxRetryPolicy : byte
    {
        NoRetry = 1,
        AlwaysRetry = 2,
    }

    /// <summary>
    /// Channel work mode (通道工作模式)。
    /// </summary>
    public enum ChannelWorkMode : byte
    {
        Normal = 0,
        ListenOnly = 1,
        Echo = 2,
    }

    /// <summary>
    /// Filter ID type: standard or extended (过滤 ID 类型：标准/扩展)。
    /// </summary>
    public enum CanFilterIDType
    {
        Standard,
        Extend,
    }

}

