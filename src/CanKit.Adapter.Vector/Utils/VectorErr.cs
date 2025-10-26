using CanKit.Adapter.Vector.Diagnostics;
using CanKit.Adapter.Vector.Native;
using CanKit.Core.Diagnostics;

namespace CanKit.Adapter.Vector.Utils;

internal static class VectorErr
{
    public static void ThrowIfError(int status, string operation, string? detail = null)
    {
        if (status == VxlApi.XL_SUCCESS)
            return;

        var text = VxlApi.GetErrorString(status);
        var message = detail ?? $"Vector native call '{operation}' failed with status {status} ({text}).";
        throw new VectorNativeException(operation, status, text, message);
    }

    public static void LogNonFatal(int status, string operation, string category = "Vector")
    {
        if (status == VxlApi.XL_SUCCESS)
            return;
        var text = VxlApi.GetErrorString(status);
        CanKitLogger.LogWarning($"{category}: native call '{operation}' completed with status {status} ({text}).");
    }

    public static string FormatStatus(int status)
    {
        var text = VxlApi.GetErrorString(status);
        return string.IsNullOrWhiteSpace(text) ? $"status={status}" : $"{text} (status={status})";
    }
}
