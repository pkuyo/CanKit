using System;
using System.Collections.Generic;
using System.Linq;
using CanKit.Core.Abstractions;
using CanKit.Core.Registry;

namespace CanKit.Core.Endpoints;

public static class BusEndpointEntry
{

    /// <summary>
    /// Try open bus by endpoint (按 endpoint 尝试打开总线)。
    /// </summary>
    public static bool TryOpen(string endpoint, Action<IBusInitOptionsConfigurator>? configure, out ICanBus? bus)
        => CanRegistry.Registry.TryOpenEndPoint(endpoint, configure, out bus);

    /// <summary>
    /// Enumerate discoverable endpoints. Optionally limit by vendor/scheme names.
    /// ZH: 枚举可发现的 Endpoint；可选传入厂商/协议前缀（如 "pcan"、"zlg"、"socketcan"）以限制范围。
    /// </summary>
    /// <param name="vendorsOrSchemes">Optional vendor or scheme names. If empty, returns all. (可选厂商/协议前缀，留空返回全部)</param>
    public static IEnumerable<BusEndpointInfo> Enumerate(params string[]? vendorsOrSchemes)
    {
        if (vendorsOrSchemes == null || vendorsOrSchemes.Length == 0)
        {
            return CanRegistry.Registry.EnumerateEndPoints(null);
        }

        static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            s = s.Trim();
            var v = s.ToLowerInvariant();
            return v switch
            {
                "peak" => "pcan",
                "pcanbasic" => "pcan",
                "pcan" => "pcan",

                "linux" => "socketcan",
                "can" => "socketcan",
                "socketcan" => "socketcan",

                "zlg" => "zlg",
                "zlglib" => "zlg",

                "kvaser" => "kvaser",
                "canlib" => "kvaser",
                _ => v
            };
        }

        var schemes = vendorsOrSchemes.Select(Normalize).ToArray();
        return CanRegistry.Registry.EnumerateEndPoints(schemes);
    }
}
