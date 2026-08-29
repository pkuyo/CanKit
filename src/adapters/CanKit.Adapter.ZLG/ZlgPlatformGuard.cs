using System;
using System.Runtime.InteropServices;

namespace CanKit.Adapter.ZLG;

/// <summary>
/// This adapter P/Invokes <c>zlgcan.dll</c> (Windows-only). Throw a clear
/// <see cref="PlatformNotSupportedException"/> before the first native call instead of
/// surfacing a raw <see cref="DllNotFoundException"/> to the caller.
/// </summary>
internal static class ZlgPlatformGuard
{
    public static void EnsureSupported()
    {
#if !FAKE
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "CanKit.Adapter.ZLG is only supported on Windows " +
                "(the ZLG native library 'zlgcan.dll' is Windows-only).");
        }
#endif
    }
}
