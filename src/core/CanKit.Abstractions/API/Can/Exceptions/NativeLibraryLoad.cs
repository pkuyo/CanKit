using System;

namespace CanKit.Core.Exceptions;

/// <summary>
/// Shared helpers for wrapping a missing or incompatible vendor native library.
/// Detects load failures only; does not load libraries and is not used by OS platform guards.
/// </summary>
public static class NativeLibraryLoad
{
    /// <summary>
    /// True when <paramref name="exception"/> (or an inner exception) is a native-image load failure.
    /// Already-wrapped <see cref="CanKitErrorCode.NativeLibraryNotFound"/> results return false.
    /// </summary>
    public static bool IsFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is CanNativeCallException { ErrorCode: CanKitErrorCode.NativeLibraryNotFound })
                return false;
            if (current is DllNotFoundException or BadImageFormatException)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Build the shared message: library name, vendor runtime, bitness, and search path.
    /// </summary>
    public static string FormatMessage(string libraryName, string vendorRuntime, Exception? cause = null)
    {
        var bitness = Environment.Is64BitProcess ? "64-bit" : "32-bit";
        var mismatch = HasBadImage(cause)
            ? $" The loaded image is invalid or does not match this {bitness} process."
            : string.Empty;

        return
            $"Native library '{libraryName}' could not be loaded.{mismatch} " +
            $"Install the {vendorRuntime} and ensure '{libraryName}' is on the DLL search path " +
            $"for this {bitness} process.";
    }

    private static bool HasBadImage(Exception? exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is BadImageFormatException)
                return true;
        }

        return false;
    }
}
