using System;
using System.Collections.Generic;
using System.Linq;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core.Definitions;
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
    /// Try prepare endpoint to construct provider + device/channel config without opening.
    /// 尝试仅构造Provider与设备/通道配置，不执行打开。
    /// </summary>
    public static bool TryPrepare(string endpoint, Action<IBusInitOptionsConfigurator>? configure, out PreparedBusContext? prepared)
        => CanRegistry.Registry.TryPrepareEndPoint(endpoint, configure, out prepared);


    /// <summary>
    /// Enumerate discoverable endpoints. Optionally limit by vendor/scheme names.
    /// (枚举可发现的 Endpoint；可选传入厂商/协议前缀（如 "pcan"、"zlg"、"socketcan"）以限制范围。)
    /// </summary>
    /// <param name="vendorsOrSchemes">Optional vendor or scheme names. If empty, returns all. (可选厂商/协议前缀，留空返回全部)</param>
    public static IEnumerable<BusEndpointInfo> Enumerate(params string[]? vendorsOrSchemes)
    {
        if (vendorsOrSchemes == null || vendorsOrSchemes.Length == 0)
        {
            return CanRegistry.Registry.EnumerateEndPoints(null);
        }

        return CanRegistry.Registry.EnumerateEndPoints(vendorsOrSchemes);
    }
}
