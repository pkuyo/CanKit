using System;
using System.Runtime.InteropServices;

namespace CanKit.Adapter.Kvaser;

/// <summary>
/// NFR-005 platform guard: this adapter P/Invokes the Kvaser CANlib native SDK
/// (<c>canlib32</c>, Windows-only). Fail with a clear
/// <see cref="PlatformNotSupportedException"/> before the first native call instead of
/// surfacing a raw <see cref="DllNotFoundException"/> to the caller.
/// </summary>
internal static class KvaserPlatformGuard
{
    public static void EnsureSupported()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "CanKit.Adapter.Kvaser is only supported on Windows " +
                "(the Kvaser CANlib native SDK 'canlib32' is Windows-only).");
        }
    }
}
