using System;

namespace Pkuyo.CanKit.ZLG.Definitions;

/// <summary>
/// 周立功 CAN 监听器报告的错误标志。
/// </summary>
[Flags]
public enum ZlgErrorFlag
{
    /// <summary>
    /// 未发生错误。
    /// </summary>
    None                  = 0x0,
    /// <summary>
    /// 接收 FIFO 溢出。
    /// </summary>
    FifoOverflow          = 0x1,
    /// <summary>
    /// 进入错误报警状态。
    /// </summary>
    ErrAlarm              = 0x2,
    /// <summary>
    /// 进入错误被动状态。
    /// </summary>
    ErrPassive            = 0x4,
    /// <summary>
    /// 发生仲裁丢失。
    /// </summary>
    ArbitrationLost       = 0x8,
    /// <summary>
    /// 检测到总线错误。
    /// </summary>
    BusErr                = 0x10,
    /// <summary>
    /// 控制器进入总线关闭状态。
    /// </summary>
    BusOff                = 0x20,
    /// <summary>
    /// 缓冲区溢出。
    /// </summary>
    BufferOverflow        = 0x40,
    /// <summary>
    /// 设备已打开。
    /// </summary>
    DeviceOpened          = 0x100,
    /// <summary>
    /// 打开设备失败。
    /// </summary>
    DeviceOpenErr         = 0x200,
    /// <summary>
    /// 设备未打开。
    /// </summary>
    DeviceNotOpen         = 0x400,
    /// <summary>
    /// 设备缓冲区溢出。
    /// </summary>
    DeviceBufferOverflow  = 0x800,
    /// <summary>
    /// 设备不存在。
    /// </summary>
    DeviceNotExist        = 0x1000,
    /// <summary>
    /// 加载驱动失败。
    /// </summary>
    LoadKernelErr         = 0x2000,
    /// <summary>
    /// 发送命令失败。
    /// </summary>
    CmdFailed             = 0x4000,
    /// <summary>
    /// 系统内存不足。
    /// </summary>
    OutOfMemory           = 0x8000
}

[Flags]
public enum ZlgFeature
{
    None         = 0x0,
    RangeFilter  = 0x1,
    MaskFilter   = 0x2,
}
