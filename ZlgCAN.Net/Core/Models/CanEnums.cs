using System;
using System.Collections.Generic;
using System.Text;

namespace ZlgCAN.Net.Core.Models
{
    public enum CanWorkMode
    {
        Normal = 0,
        ListenOnly = 1
    }

    public enum CanType
    {
        UsbCan = 0,
        NetCan
    }
   

    [Flags]
    public enum CanFrameFlag : uint
    {
        Invalid = 0,
        ClassicCan = 1,
        CanFd = 1 << 1,
        Error = 1 << 2,
        Gps = 1 << 3,           // GPS数据
        Lin = 1 << 4,           // LIN数据
        BusStage = 1 << 5,      // BusUsage数据
        LinError = 1 << 6,      // LIN错误数据
        LinEx = 1 << 7,         // LIN扩展数据
        LinEvent = 1 << 8,
        Any = 0x1FF,
    }

    internal enum ReceviceCanFrameKind : byte
    {
        ClassicOrFd = 1,    // CAN/CANFD数据
        Error = 2,          // 错误数据
        Gps = 3,            // GPS数据
        Lin = 4,            // LIN数据
        BusStage = 5,       // BusUsage数据
        LinError = 6,       // LIN错误数据
        LinEx = 7,          // LIN扩展数据
        LinEvent = 8,  
    }

    
}
