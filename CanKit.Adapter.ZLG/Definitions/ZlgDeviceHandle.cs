using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.ZLG.Native;

namespace CanKit.Adapter.ZLG.Definitions;

public sealed class ZlgDeviceHandle : SafeHandle
{
    public ZlgDeviceHandle() : base(IntPtr.Zero, true)
    {

    }

    public ZlgDeviceHandle(IntPtr ptr) : base(ptr, true)
    {

    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        var result = ZLGCAN.ZCAN_CloseDevice(handle) != 0;
        SetHandleAsInvalid();
        return result;
    }
}

public sealed class ZlgChannelHandle() : SafeHandle(IntPtr.Zero, false)
{
    public IntPtr DeviceHandle { get; private set; }

    public override bool IsInvalid => handle == IntPtr.Zero || DeviceHandle == IntPtr.Zero;

    public void SetDevice(IntPtr deviceHandle)
    {
        DeviceHandle = deviceHandle;
    }

    protected override bool ReleaseHandle() => true;
}
