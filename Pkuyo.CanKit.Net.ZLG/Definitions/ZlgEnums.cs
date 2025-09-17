using System;

namespace Pkuyo.CanKit.ZLG.Definitions;

[Flags]
public enum ZlgErrorFlag
{
    None                  = 0x0,
    FifoOverflow          = 0x1,
    ErrAlarm              = 0x2,
    ErrPassive            = 0x4,
    ArbitrationLost       = 0x8,
    BusErr                = 0x10,
    BusOff                = 0x20,
    BufferOverflow        = 0x40,
    DeviceOpened          = 0x100,
    DeviceOpenErr         = 0x200,
    DeviceNotOpen         = 0x400,
    DeviceBufferOverflow  = 0x800,
    DeviceNotExist        = 0x1000,
    LoadKernelErr         = 0x2000,
    CmdFailed             = 0x4000,
    OutOfMemory           = 0x8000
}