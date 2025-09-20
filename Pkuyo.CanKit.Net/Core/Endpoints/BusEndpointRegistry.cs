using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;

namespace Pkuyo.CanKit.Net.Core.Endpoints;

/// <summary>
/// Registry mapping endpoint schemes to open handlers (将 endpoint scheme 映射到打开处理器的注册表)。
/// Provider 应在模块/类型初始化时注册 handler。
/// </summary>
public static class BusEndpointRegistry
{
    private static readonly Dictionary<string, Func<CanEndpoint, Action<IChannelInitOptionsConfigurator>?, ICanBus>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a scheme handler (注册一个 scheme 处理器)。
    /// </summary>
    public static void Register(string scheme, Func<CanEndpoint, Action<IChannelInitOptionsConfigurator>?, ICanBus> openHandler)
    {
        if (string.IsNullOrWhiteSpace(scheme)) throw new ArgumentNullException(nameof(scheme));
        if (openHandler == null) throw new ArgumentNullException(nameof(openHandler));
        _handlers[scheme] = openHandler;
    }

    /// <summary>
    /// Try open bus by endpoint (按 endpoint 尝试打开总线)。
    /// </summary>
    public static bool TryOpen(string endpoint, Action<IChannelInitOptionsConfigurator>? configure, out ICanBus? bus)
    {
        var ep = CanEndpoint.Parse(endpoint);
        if (_handlers.TryGetValue(ep.Scheme, out var h))
        {
            bus = h(ep, configure);
            return true;
        }
        bus = null;
        return false;
    }
}
