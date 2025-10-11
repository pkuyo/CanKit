using CanKit.Adapter.PCAN.Definitions;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Endpoints;
using System;
using System.Collections.Generic;
using System.Reflection;
using Peak.Can.Basic;
using Peak.Can.Basic.BackwardCompatibility;

namespace CanKit.Adapter.PCAN;

/// <summary>
/// Registers endpoint handler for scheme "pcan".
/// Examples:
///  - pcan://PCAN_USBBUS1
///  - pcan://?ch=PCAN_PCIBUS1
/// </summary>
[CanEndPoint("pcan", ["pcanbasic", "peak"])]
internal static class PcanEndpoint
{
    public static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        string ch = ep.Path;
        if (string.IsNullOrWhiteSpace(ch))
        {
            if (ep.TryGet("ch", out var v) || ep.TryGet("channel", out v) || ep.TryGet("bus", out v))
                ch = v!;
        }

        return CanBus.Open<PcanBus, PcanBusOptions, PcanBusInitConfigurator>(
            PcanDeviceType.PCANBasic,
            cfg =>
            {
                cfg.UseChannelName(ch);
                configure?.Invoke(cfg);
            });
    }

    /// <summary>
    /// Enumerate available PCAN channels using PCAN-Basic, if present. Fails safe to empty.
    /// ZH: 通过 PCAN-Basic 枚举可用通道；失败则返回空列表。
    /// </summary>
    public static IEnumerable<BusEndpointInfo> Enumerate()
    {
        var sts = Api.GetAttachedChannels(out PcanChannelInformation[] chans);
        if (sts != PcanStatus.OK)
            yield break;
        foreach (var chan in chans)
        {
            yield return new BusEndpointInfo
            {
                DeviceType = PcanDeviceType.PCANBasic,
                Endpoint = $"pcan://{chan.ChannelHandle}",
                Title = $"{chan.DeviceName} {chan.ChannelHandle} (PCAN)",
                Meta = new Dictionary<string, string>
                {
                    { "name", chan.DeviceName },
                    { "type", chan.DeviceType.ToString() },
                }
            };
        }
    }
}
