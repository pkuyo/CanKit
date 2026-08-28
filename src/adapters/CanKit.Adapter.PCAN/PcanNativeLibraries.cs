using System.Runtime.InteropServices;

namespace CanKit.Adapter.PCAN;

/// <summary>
/// Names of Peak native libraries loaded by this adapter. Used only in load-failure messages;
/// this type does not load the libraries.
/// </summary>
internal static class PcanNativeLibraries
{
    public static string BasicLibraryName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "PCANBasic" : "libpcanbasic";

    public const string BasicVendorRuntime = "Peak PCAN-Basic runtime";

    public const string IsoTpLibraryName = "PCAN-ISO-TP.dll";

    public const string IsoTpVendorRuntime = "Peak PCAN-ISO-TP runtime";
}
