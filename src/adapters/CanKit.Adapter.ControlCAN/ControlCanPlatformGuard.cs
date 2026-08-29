using System;
using System.Runtime.InteropServices;

namespace CanKit.Adapter.ControlCAN;

/// <summary>
/// This adapter P/Invokes the vendor ControlCAN native driver (Windows-only). Throw a clear
/// <see cref="PlatformNotSupportedException"/> before the first native call instead of
/// surfacing a raw <see cref="DllNotFoundException"/> to the caller.
/// </summary>
internal static class ControlCanPlatformGuard
{
    public static void EnsureSupported()
    {
#if !FAKE
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "CanKit.Adapter.ControlCAN is only supported on Windows " +
                "(the vendor ControlCAN native driver is Windows-only).");
        }
#endif
    }
}
