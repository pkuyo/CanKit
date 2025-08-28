using System;
using System.Collections.Generic;
using System.Text;

namespace ZlgCAN.Net.Core.Definitions
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
        LinEvent = 1 << 8
    }

    public enum CanFilterType : uint
    {
        Can = 0,
        Fd = 1,
        Any = 2,
        Lin = 3,
    };
    
    [Flags]
    public enum CanValueAccess : uint
    {
        Get = 1,
        Set = 2,
        GetSet = 3
    }

}
