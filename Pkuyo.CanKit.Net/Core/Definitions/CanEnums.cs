using System;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    public enum CanErrorCode
    {
        None = 0,          // 没有错误

        // 基础 CAN 错误类型
        BitError,          // 位错误（Bit Error）
        StuffError,        // 填充位错误（Stuff Error）
        CrcError,          // CRC 校验错误
        FormError,         // 帧格式错误（Form Error）
        AckError,          // 没收到 ACK

        // 总线级错误
        BusOff,            // 控制器进入 Bus Off
        BusHeavy,          // 总线重载（Error Passive/Bus Heavy）
        Restarted,         // 控制器重启

        // 缓冲区/队列类错误
        Overflow,          // 缓冲区溢出（Rx FIFO Overflow）
        QueueEmpty,        // 队列空（主要是 PCANBasic 用）

        // 其他错误
        ProtocolError,     // 协议错误（未细分的 SocketCAN CAN_ERR_PROT）
        Other              // 其他未知或未分类错误
    }
    [Flags]
    public enum CanFrameType : uint
    {
        Invalid = 0,
        CanClassic = 1,
        CanFd = 1 << 1,
        Error = 1 << 2,
        Any = int.MaxValue
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
    
    public enum FilterIDType
    {
        Standard,
        Extend,
    }

}
