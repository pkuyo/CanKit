using System;
using System.Runtime.InteropServices;

namespace CanKit.Adapter.PCAN;

/// <summary>
/// Peak ships PCAN-Basic for Windows and Linux only. Throw a clear
/// <see cref="PlatformNotSupportedException"/> before the first native call instead of
/// surfacing a raw <see cref="DllNotFoundException"/> to the caller.
/// </summary>
internal static class PcanPlatformGuard
{
    public static void EnsureSupported()
    {
#if !FAKE
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException(
                "CanKit.Adapter.PCAN is only supported on Windows and Linux " +
                "(the Peak PCAN-Basic native library).");
        }
#endif
    }
}
