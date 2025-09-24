using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Registry;

namespace Pkuyo.CanKit.Net.Core.Endpoints;

public static class BusEndpointEntry
{

    /// <summary>
    /// Try open bus by endpoint (按 endpoint 尝试打开总线)。
    /// </summary>
    public static bool TryOpen(string endpoint, Action<IBusInitOptionsConfigurator>? configure, out ICanBus? bus)
        => CanRegistry.TryOpenEndPoint(endpoint, configure, out bus);
}
