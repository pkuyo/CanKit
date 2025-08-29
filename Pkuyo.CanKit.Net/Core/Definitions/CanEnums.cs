using System;

namespace Pkuyo.CanKit.Net.Core.Definitions
{

    [Flags]
    public enum CanFrameType : uint
    {
        Invalid = 0,
        CanClassic = 1,
        CanFd = 1 << 1,
        Error = 1 << 2,
        Gps = 1 << 3,           // GPS数据
        Lin = 1 << 4,           // LIN数据
        BusStage = 1 << 5,      // BusUsage数据
        LinError = 1 << 6,      // LIN错误数据
        LinEx = 1 << 7,         // LIN扩展数据
        LinEvent = 1 << 8,
        Any = int.MaxValue
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

    public enum TxRetryPolicy
    {
        NoRetry = 0,
        AlwaysRetry = 1,
    }
    
    public enum ChannelWorkMode
    {
        Normal = 0,
        ListenOnly = 1,
        Echo = 2,
    }
    
    public enum FilterMode
    {
        Standard,
        Extend,
    }

}
