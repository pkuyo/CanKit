using System;

namespace Pkuyo.CanKit.Net.Core.Definitions
{

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

    public enum FrameDirection
    {
        Unknown = 0,
        Tx,
        Rx
    }
    
    public enum ChannelErrorState
    {
        None        = 0,         // 没有错误
        AlarmErr    = 1,         // 控制器到达警告阈值
        PassiveErr  = 2,         // 进入被动错误
        BusOff      = 3,         // 总线关闭
        BusRestart  = 4,         // 总线重启
    }
    
    [Flags]
    public enum CanFrameType : uint
    {
        Invalid    = 0,
        CanClassic = 1 << 0,
        CanFd      = 1 << 1,
        Error      = 1 << 2,
        Any        = int.MaxValue
    }
    
    public enum CanProtocolMode
    {
        Can20 = 0,   
        CanFd = 1,   
        Merged = 2   
    }

    
    [Flags]
    public enum CanFeature
    {
        CanClassic          = 1 << 0,
        CanFd               = 1 << 1,
        Ethernet            = 1 << 2,
        
        CyclicTx            = 1 << 3,
        BatchingTx          = 1 << 4,
        
        MergeReceive        = 1 << 5,
        ErrorCounters       = 1 << 6,
        
        Filters             = 1 << 7,
        BusUsage            = 1 << 8
    }

    public enum CanOptionType
    {
        Init = 1,
        Runtime = 2
    }

    public enum TxRetryPolicy : byte
    {
        NoRetry = 1,
        AlwaysRetry = 2,
    }
    
    public enum ChannelWorkMode : byte
    {
        Normal = 0,
        ListenOnly = 1,
        Echo = 2,
    }
    
    public enum CanFilterIDType
    {
        Standard,
        Extend,
    }

}
