using System.Runtime.InteropServices;

namespace CanKit.Adapter.Kvaser;

/// <summary>
/// Names of the Kvaser CANlib native library this adapter P/Invokes.
/// Used in load-failure messages; this type does not load the library.
/// </summary>
internal static class KvaserNativeLibraries
{
    internal const string WindowsLibraryName = "canlib32";

    internal const string LinuxLibraryName = "libcanlib.so";

    internal const string VendorRuntime = "Kvaser CANlib";

    internal static bool IsLinux { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    internal static string LibraryName => IsLinux ? LinuxLibraryName : WindowsLibraryName;
}
