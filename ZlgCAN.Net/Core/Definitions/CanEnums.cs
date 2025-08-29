using System;
using System.Collections.Generic;
using System.Text;

namespace ZlgCAN.Net.Core.Definitions
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
        AutoSend            = 1 << 3,
        QueueSend           = 1 << 4,
        MergeReceive        = 1 << 5,
        CustomSerialNumber  = 1 << 6,
        Filters             = 1 << 7,
        AccMask             = 1 << 8,
        BusUsage            = 1 << 9,
        TxRetryPolicy       = 1 << 10,
        SendType            = 1 << 11,
    }
    
    public enum EthernetWorkMode
    {
        
    }
    public enum ChannelWorkMode
    {
        Normal = 0,
        ListenOnly = 1
    }

}
