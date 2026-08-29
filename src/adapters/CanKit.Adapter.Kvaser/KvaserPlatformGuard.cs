using System;
using System.Runtime.InteropServices;

namespace CanKit.Adapter.Kvaser;

/// <summary>
/// This adapter binds Kvaser CANlib as <c>canlib32</c> (Windows, stdcall) and
/// <c>libcanlib.so</c> (Linux, cdecl). Throw a clear
/// <see cref="PlatformNotSupportedException"/> before the first native call
/// instead of surfacing a raw <see cref="DllNotFoundException"/> on an OS this
/// binding does not cover.
/// </summary>
internal static class KvaserPlatformGuard
{
    public static void EnsureSupported()
    {
#if !FAKE
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException(
                "CanKit.Adapter.Kvaser is only supported on Windows and Linux " +
                "(this adapter binds Kvaser CANlib as canlib32 on Windows and libcanlib.so on Linux).");
        }
#endif
    }
}
