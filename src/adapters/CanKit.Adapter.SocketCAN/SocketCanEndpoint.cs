using CanKit.Adapter.SocketCAN.Definitions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core;
using CanKit.Core.Diagnostics;
using CanKit.Core.Endpoints;
using CanKit.Core.Exceptions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.SocketCAN;

/// <summary>
/// Registers endpoint handler for scheme "socketcan" (为 "socketcan" scheme 注册 Endpoint 处理器)。
/// </summary>
internal static class SocketCanEndpoint
{
    public static PreparedBusContext Prepare(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        string iface = ep.Path;
        int? rxBuf = null;
        int? txBuf = null;
        if (string.IsNullOrWhiteSpace(iface))
        {
            if (ep.TryGet("if", out var v) || ep.TryGet("iface", out v))
                iface = v!;
        }
        if (ep.TryGet("rxbuf", out var u))
        {
            if (int.TryParse(u, out var result))
                rxBuf = result;
        }
        if (ep.TryGet("txbuf", out var u1))
        {
            if (int.TryParse(u1, out var result))
                txBuf = result;
        }
        bool enableNetLink = false;
        if (!string.IsNullOrWhiteSpace(ep.Fragment))
        {
            var frag = ep.Fragment!;
            if (frag.StartsWith("netlink", StringComparison.OrdinalIgnoreCase) ||
                frag.StartsWith("nl", StringComparison.OrdinalIgnoreCase))
            {
                enableNetLink = true;
            }
        }

        var provider = CanRegistry.Registry.Resolve(LinuxDeviceType.SocketCAN);
        var (devOpt, devCfg) = provider.GetDeviceOptions();
        var (chOpt, chCfg) = provider.GetChannelOptions();
        var typed = (SocketCanBusInitConfigurator)chCfg;
        typed.UseChannelName(iface).NetLink(enableNetLink);
        if (rxBuf != null) typed.ReceiveBufferCapacity(rxBuf.Value);
        if (txBuf != null) typed.TransmitBufferCapacity(txBuf.Value);
        configure?.Invoke(typed);
        return new PreparedBusContext(provider, devOpt, devCfg, chOpt, chCfg);
    }

    public static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        var (provider, devOpt, _, chOpt, chCfg) = Prepare(ep, configure);

        var device = provider.Factory.CreateDevice(devOpt);
        if (device == null)
        {
            throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"Factory '{provider.Factory.GetType().FullName}' returned null device.");
        }
        return CanBus.Open<SocketCanBus, SocketCanBusOptions, SocketCanBusInitConfigurator>
            (device, (SocketCanBusOptions)chOpt, (SocketCanBusInitConfigurator)chCfg);
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
