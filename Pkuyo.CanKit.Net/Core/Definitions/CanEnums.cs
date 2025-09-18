using System;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    /// <summary>
    /// 表示 CAN 帧在传输过程中可能发生的错误类型。
    /// </summary>
    public enum FrameErrorKind
    {
        None        = 0,         // 没有错误
        BitError    = 1,         // 位错误（Bit Error）
        StuffError  = 2,         // 填充位错误（Stuff Error）
        CrcError    = 3,         // CRC 校验错误
        FormError   = 4,         // 帧格式错误（Form Error）
        AckError    = 5,         // 没收到 ACK
        Controller  = 6,
        
        Unknown     = 65535
    }

    /// <summary>
    /// 表示数据帧的方向，是发送帧还是接收帧。
    /// </summary>
    public enum FrameDirection
    {
        Unknown = 0,
        Tx,
        Rx
    }

    /// <summary>
    /// 表示 CAN 通道当前的错误状态。
    /// </summary>
    public enum ChannelErrorState
    {
        None        = 0,         // 没有错误
        AlarmErr    = 1,         // 控制器到达警告阈值
        PassiveErr  = 2,         // 进入被动错误
        BusOff      = 3,         // 总线关闭
        BusRestart  = 4,         // 总线重启
    }
    
    /// <summary>
    /// 用于标识 CAN 帧类型的标志位，可组合使用。
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
    /// 描述硬件支持的 CAN 协议模式。
    /// </summary>
    public enum CanProtocolMode
    {
        Can20 = 0,
        CanFd = 1,
        Merged = 2
    }

    /// <summary>
    /// 描述监听器所支持的能力集，可组合使用。
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
    /// 控制配置选项所属的阶段。
    /// </summary>
    public enum CanOptionType
    {
        Init = 1,
        Runtime = 2
    }

    /// <summary>
    /// 发送失败后 CAN 控制器的重试策略。
    /// </summary>
    public enum TxRetryPolicy : byte
    {
        NoRetry = 1,
        AlwaysRetry = 2,
    }

    /// <summary>
    /// 表示 CAN 通道的工作模式，例如正常模式或只听模式。
    /// </summary>
    public enum ChannelWorkMode : byte
    {
        Normal = 0,
        ListenOnly = 1,
        Echo = 2,
    }

    /// <summary>
    /// 指示过滤规则使用标准帧 ID 还是扩展帧 ID。
    /// </summary>
    public enum CanFilterIDType
    {
        Standard,
        Extend,
    }

}
