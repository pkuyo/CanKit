using CanKit.Adapter.SocketCAN.Definitions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Diagnostics;
using CanKit.Core.Endpoints;

namespace CanKit.Adapter.SocketCAN;

/// <summary>
/// Registers endpoint handler for scheme "socketcan" (为 "socketcan" scheme 注册 Endpoint 处理器)。
/// </summary>

[CanEndPoint("socketcan", ["linux", "libsocketcan"])]
internal static class SocketCanEndpoint
{
    public static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        // Interpret path as interfaceName, or use query iface= / if=, else index as number
        string iface = ep.Path;
        int index = 0;
        uint? rcvbuf = null;
        if (string.IsNullOrWhiteSpace(iface))
        {
            if (ep.TryGet("if", out var v) || ep.TryGet("iface", out v))
                iface = v!;

            // Use query rcvbuf= / rcvbuf= to set receive buffer cap.
            if (ep.TryGet("rcvbuf", out var u))
            {
                if(uint.TryParse(u, out var result))
                    rcvbuf = result;
                else
                {
                    CanKitLogger.LogError($"SocketCAN: Invalid rcvbuf value:{u}");
                }
            }
        }

        bool enableNetLink = false;
        // use #netlink/#nl to conifg CanBus by libsocketcan
        if (!string.IsNullOrWhiteSpace(ep.Fragment))
        {
            var frag = ep.Fragment!;
            if (frag.StartsWith("netlink", StringComparison.OrdinalIgnoreCase) ||
                frag.StartsWith("nl", StringComparison.OrdinalIgnoreCase))
            {
                enableNetLink = true;
            }
        }


        return CanBus.Open<SocketCanBus, SocketCanBusOptions, SocketCanBusInitConfigurator>(
            LinuxDeviceType.SocketCAN,
            cfg =>
            {
                cfg.UseChannelName(iface)
                    .NetLink(enableNetLink)
                    .SetReceiveBufferCapacity(rcvbuf);
                configure?.Invoke(cfg);
            });
    }

    /// <summary>
    /// Enumerate available SocketCAN interfaces on Linux.
    /// ZH: 在 Linux 上枚举可用的 SocketCAN 接口。
    /// </summary>
    public static IEnumerable<BusEndpointInfo> Enumerate()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) yield break;
        var sys = "/sys/class/net";
        if (!Directory.Exists(sys)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(sys))
        {
            string name = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (name.StartsWith("can", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("vcan", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("vxcan", StringComparison.OrdinalIgnoreCase))
            {
                yield return new BusEndpointInfo
                {
                    Scheme = "socketcan",
                    Endpoint = $"socketcan://{name}",
                    Title = $"{name} (SocketCAN)",
                    DeviceType = LinuxDeviceType.SocketCAN,
                    Meta = new Dictionary<string, string> { { "iface", name } }
                };
            }
        }
    }
}
