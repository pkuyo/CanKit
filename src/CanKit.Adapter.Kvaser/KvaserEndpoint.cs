using CanKit.Adapter.Kvaser.Definitions;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Endpoints;
using System;
using System.Collections.Generic;
using CanKit.Core.Diagnostics;
using CanKit.Core.Registry;
using CanKit.Adapter.Kvaser.Native;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.Kvaser;

/// <summary>
/// Registers endpoint handler for scheme "kvaser".
/// Examples:
///  - kvaser://0          (open by channel number)
///  - kvaser://?ch=1      (open by channel number)
///  - kvaser:1            (fallback form)
/// </summary>
[CanEndPoint("kvaser", ["canlib"])]
internal static class KvaserEndpoint
{
    public static PreparedBusContext Prepare(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        int channel = 0;
        int? rxBuf = null;
        int ts = 1000;
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

        if (ep.TryGet("rxbuf", out var v1))
        {
            if (int.TryParse(v1, out var rx))
            {
                rxBuf = rx;
            }
        }
        if (ep.TryGet("ts", out v1))
        {
            if (int.TryParse(v1, out var rx))
            {
                ts = rx;
            }
        }

        var provider = CanRegistry.Registry.Resolve(KvaserDeviceType.CANlib);
        var (devOpt, devCfg) = provider.GetDeviceOptions();
        var (chOpt, chCfg) = provider.GetChannelOptions();
        var typed = (KvaserBusInitConfigurator)chCfg;
        typed.UseChannelIndex(channel).TimerScaleMicroseconds(ts).ReceiveBufferCapacity(rxBuf);
        configure?.Invoke(chCfg);
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
        return CanBus.Open<KvaserBus, KvaserBusOptions, KvaserBusInitConfigurator>(
            device, (KvaserBusOptions)chOpt, (KvaserBusInitConfigurator)chCfg);
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
            if (Canlib.canGetNumberOfChannels(out var n) == Canlib.canStatus.canOK)
            {
                for (int i = 0; i < n; i++)
                {
                    string? name = null;
                    string? ean = null;
                    string? serial = null;
                    try
                    {
                        if (Canlib.GetChannelName(i, out var s) == Canlib.canStatus.canOK)
                            name = s;
                    }
                    catch { }
                    try
                    {
                        if (Canlib.GetEanString(i, Canlib.canCHANNELDATA_CARD_UPC_NO, out var e) == Canlib.canStatus.canOK)
                            ean = e;
                    }
                    catch { }
                    try
                    {
                        if (Canlib.GetUInt32Pair(i, Canlib.canCHANNELDATA_CARD_SERIAL_NO, out uint hi, out uint lo) == Canlib.canStatus.canOK)
                            serial = $"0x{hi:X8}{lo:X8}";
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
