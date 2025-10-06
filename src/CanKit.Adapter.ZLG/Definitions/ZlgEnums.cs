using System;

namespace CanKit.Adapter.ZLG.Definitions;

/// <summary>
/// ZLG listener error flags (ZLG 错误标志)。
/// </summary>
[Flags]
public enum ZlgErrorFlag
{
    /// <summary>
    /// No error (无错误)。
    /// </summary>
    None = 0x0,
    /// <summary>
    /// RX FIFO overflow (接收 FIFO 溢出)。
    /// </summary>
    FifoOverflow = 0x1,
    /// <summary>
    /// Error alarm (进入错误报警)。
    /// </summary>
    ErrAlarm = 0x2,
    /// <summary>
    /// Error passive (进入错误被动)。
    /// </summary>
    ErrPassive = 0x4,
    /// <summary>
    /// Arbitration lost (仲裁丢失)。
    /// </summary>
    ArbitrationLost = 0x8,
    /// <summary>
    /// Bus error (总线错误)。
    /// </summary>
    BusErr = 0x10,
    /// <summary>
    /// Bus-off (控制器进入总线关闭)。
    /// </summary>
    BusOff = 0x20,
    /// <summary>
    /// Buffer overflow (缓冲区溢出)。
    /// </summary>
    BufferOverflow = 0x40,
    /// <summary>
    /// Device opened (设备已打开)。
    /// </summary>
    DeviceOpened = 0x100,
    /// <summary>
    /// Open device failed (打开设备失败)。
    /// </summary>
    DeviceOpenErr = 0x200,
    /// <summary>
    /// Device not open (设备未打开)。
    /// </summary>
    DeviceNotOpen = 0x400,
    /// <summary>
    /// Device buffer overflow (设备缓冲区溢出)。
    /// </summary>
    DeviceBufferOverflow = 0x800,
    /// <summary>
    /// Device not exist (设备不存在)。
    /// </summary>
    DeviceNotExist = 0x1000,
    /// <summary>
    /// Load driver failed (加载驱动失败)。
    /// </summary>
    LoadKernelErr = 0x2000,
    /// <summary>
    /// Command failed (发送命令失败)。
    /// </summary>
    CmdFailed = 0x4000,
    /// <summary>
    /// Out of memory (内存不足)。
    /// </summary>
    OutOfMemory = 0x8000
}

[Flags]
public enum ZlgFeature
{
    None = 0x0,
    RangeFilter = 0x1,
    MaskFilter = 0x2,
}

