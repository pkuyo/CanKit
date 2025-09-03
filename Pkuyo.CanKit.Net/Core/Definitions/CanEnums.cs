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

    public enum TxRetryPolicy
    {
        NoRetry = 1,
        AlwaysRetry = 2,
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
