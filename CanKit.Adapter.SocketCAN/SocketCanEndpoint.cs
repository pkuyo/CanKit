using CanKit.Adapter.SocketCAN.Definitions;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Endpoints;

namespace CanKit.Adapter.SocketCAN;

/// <summary>
/// Registers endpoint handler for scheme "socketcan" (为 "socketcan" scheme 注册 Endpoint 处理器)。
/// </summary>

[CanEndPoint("socketcan")]
internal static class SocketCanEndpoint
{
    public static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
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

        return CanBus.Open<SocketCanBus, SocketCanBusOptions, SocketCanBusInitConfigurator>(
            LinuxDeviceType.SocketCAN,
            index,
            cfg =>
            {
                cfg.UseInterface(iface);
                configure?.Invoke(cfg);
            });
    }
}
