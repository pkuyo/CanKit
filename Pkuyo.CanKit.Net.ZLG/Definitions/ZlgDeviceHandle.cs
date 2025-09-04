using System;
using System.Runtime.InteropServices;
using Pkuyo.CanKit.ZLG.Native;

namespace Pkuyo.CanKit.ZLG.Definitions;

public class ZlgDeviceHandle : SafeHandle
{
    public ZlgDeviceHandle() : base(IntPtr.Zero, true)
    {
        
    }
    public ZlgDeviceHandle(IntPtr ptr) : base(ptr, true)
    {
        
    }
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
    public void SetDevice(IntPtr deviceHandle)
    {
        DeviceHandle = deviceHandle;
    }
    
    public IntPtr DeviceHandle { get; private set; }
    
    protected override bool ReleaseHandle() => true;

    public override bool IsInvalid => handle == IntPtr.Zero || DeviceHandle == IntPtr.Zero;
}