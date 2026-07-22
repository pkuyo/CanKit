using System;
using System.Runtime.InteropServices;

namespace CanKit.Adapter.ControlCAN;

/// <summary>
/// NFR-005 platform guard: this adapter P/Invokes the vendor's ControlCAN native driver
/// (Windows-only). Fail with a clear <see cref="PlatformNotSupportedException"/> before the
/// first native call instead of surfacing a raw <see cref="DllNotFoundException"/> to the
/// caller.
/// </summary>
internal static class ControlCanPlatformGuard
{
    public static void EnsureSupported()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "CanKit.Adapter.ControlCAN is only supported on Windows " +
                "(the vendor ControlCAN native driver is Windows-only).");
        }
    }
}
