using System;
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
}
