using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Endpoints;
using Pkuyo.CanKit.SocketCAN.Definitions;

namespace Pkuyo.CanKit.Net.SocketCAN;

/// <summary>
/// Registers endpoint handler for scheme "socketcan" (为 "socketcan" scheme 注册 Endpoint 处理器)。
/// </summary>
internal static class SocketCanEndpoint
{
    static SocketCanEndpoint()
    {
        BusEndpointRegistry.Register("socketcan", Open);
    }

    private static ICanBus Open(CanEndpoint ep, Action<IChannelInitOptionsConfigurator>? configure)
    {
        // Interpret path as interfaceName, or use query iface= / if=, else index as number
        string iface = ep.Path;
        int index = 0;
        if (string.IsNullOrWhiteSpace(iface))
        {
            if (ep.TryGet("if", out var v) || ep.TryGet("iface", out v))
                iface = v!;
        }

        if (string.IsNullOrWhiteSpace(iface))
        {
            // try index
            if (int.TryParse(ep.Path, out var idx)) index = idx;
            iface = $"can{index}";
        }

        return CanBus.Open<SocketCanChannel, SocketCanChannelOptions, SocketCanChannelInitConfigurator>(
            LinuxDeviceType.SocketCAN,
            index,
            cfg =>
            {
                cfg.UseInterface(iface);
                configure?.Invoke(cfg);
            });
    }
}
