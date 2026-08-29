using System;
using System.Runtime.InteropServices;

namespace CanKit.Adapter.Vector;

/// <summary>
/// This adapter P/Invokes the Vector XL Driver Library (<c>vxlapi64</c>/<c>vxlapi</c>,
/// Windows-only). Throw a clear <see cref="PlatformNotSupportedException"/> before the first
/// native call instead of surfacing a raw <see cref="DllNotFoundException"/> to the caller.
/// </summary>
internal static class VectorPlatformGuard
{
    public static void EnsureSupported()
    {
#if !FAKE
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "CanKit.Adapter.Vector is only supported on Windows " +
                "(the Vector XL Driver Library 'vxlapi64/vxlapi' is Windows-only).");
        }
#endif
    }
}
