using System;
using System.Runtime.InteropServices;
using Pkuyo.CanKit.ZLG.Native;

namespace Pkuyo.CanKit.ZLG.Definitions;

public class ZlgDeviceHandle() : SafeHandle(IntPtr.Zero, true)
{
    protected override bool ReleaseHandle()
    {
        var result = ZLGCAN.ZCAN_CloseDevice(handle) != 0;
        SetHandleAsInvalid();
        return result;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
}

public class ZlgChannelHandle() : SafeHandle(IntPtr.Zero, false)
{
    public void SetDevice(ZlgDeviceHandle deviceHandle)
    {
        DeviceHandle = deviceHandle;
    }
    
    public ZlgDeviceHandle DeviceHandle { get; private set; }
    
    protected override bool ReleaseHandle() => true;

    public override bool IsInvalid => handle == IntPtr.Zero || DeviceHandle.IsInvalid;
}