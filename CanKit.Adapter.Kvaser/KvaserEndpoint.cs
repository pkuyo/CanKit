using CanKit.Adapter.Kvaser.Definitions;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Endpoints;
using System;
using System.Collections.Generic;
using Kvaser.CanLib;

namespace CanKit.Adapter.Kvaser;

/// <summary>
/// Registers endpoint handler for scheme "kvaser".
/// Examples:
///  - kvaser://0          (open by channel number)
///  - kvaser://?ch=1      (open by channel number)
///  - kvaser:1            (fallback form)
/// </summary>
[CanEndPoint("kvaser")]
internal static class KvaserEndpoint
{
    public static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        int channel = 0;
        if (!string.IsNullOrWhiteSpace(ep.Path))
        {
            _ = int.TryParse(ep.Path, out channel);
        }
        if (channel == 0)
        {
            if (ep.TryGet("ch", out var v) || ep.TryGet("channel", out v) || ep.TryGet("bus", out v))
            {
                _ = int.TryParse(v, out channel);
            }
        }

        return CanBus.Open<KvaserBus, KvaserBusOptions, KvaserBusInitConfigurator>(
            KvaserDeviceType.CANlib,
            cfg =>
            {
                cfg.UseChannelIndex(channel);
                configure?.Invoke(cfg);
            });
    }

    /// <summary>
    /// Enumerate Kvaser CANlib channels.
    /// ZH: 枚举 Kvaser CANlib 的通道。
    /// </summary>
    public static IEnumerable<BusEndpointInfo> Enumerate()
    {
        var list = new List<BusEndpointInfo>();
        try { Canlib.canInitializeLibrary(); } catch { }
        try
        {
            if (Canlib.canGetNumberOfChannels(out int n) == Canlib.canStatus.canOK)
            {
                for (int i = 0; i < n; i++)
                {
                    string? name = null;
                    string? ean = null;
                    string? serial = null;
                    try
                    {
                        if (Canlib.canGetChannelData(i, Canlib.canCHANNELDATA_CHANNEL_NAME, out var obj) == Canlib.canStatus.canOK && obj is string s)
                            name = s;
                    }
                    catch { }
                    try
                    {
                        if (Canlib.canGetChannelData(i, Canlib.canCHANNELDATA_CARD_UPC_NO, out var objEan) == Canlib.canStatus.canOK && objEan is string e)
                            ean = e;
                    }
                    catch { }
                    try
                    {
                        if (Canlib.canGetChannelData(i, Canlib.canCHANNELDATA_CARD_SERIAL_NO, out var objSn) == Canlib.canStatus.canOK && objSn is string sn)
                            serial = sn;
                    }
                    catch { }

                    var meta = new Dictionary<string, string> { { "channel", i.ToString() } };
                    if (!string.IsNullOrWhiteSpace(name)) meta["name"] = name!;
                    if (!string.IsNullOrWhiteSpace(ean)) meta["ean"] = ean!;
                    if (!string.IsNullOrWhiteSpace(serial)) meta["serial"] = serial!;

                    list.Add(new BusEndpointInfo
                    {
                        Scheme = "kvaser",
                        Endpoint = $"kvaser://{i}",
                        Title = name is null ? $"ch{i} (Kvaser)" : $"{name} ch{i} (Kvaser)",
                        DeviceType = KvaserDeviceType.CANlib,
                        Meta = meta
                    });
                }
            }
        }
        catch
        {
            // swallow – best effort
        }
        return list;
    }
}
