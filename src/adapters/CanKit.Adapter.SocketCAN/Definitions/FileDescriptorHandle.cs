using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.SocketCAN.Native;
using Microsoft.Win32.SafeHandles;

namespace CanKit.Adapter.SocketCAN.Definitions;

internal sealed class FileDescriptorHandle : SafeHandleMinusOneIsInvalid
{
    public FileDescriptorHandle() : base(true) { }

    public FileDescriptorHandle(IntPtr preexistingHandle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(preexistingHandle);
    }

    protected override bool ReleaseHandle()
    {
        return Libc.close(handle.ToInt32()) == 0;
    }
}

