using CanKit.Adapter.PCAN.Definitions;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Endpoints;
using CanKit.Core.Registry;
using System;
using System.Collections.Generic;
using System.Reflection;
using CanKit.Core.Exceptions;
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
    public static PreparedBusContext Prepare(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        string ch = ep.Path;
        if (string.IsNullOrWhiteSpace(ch))
        {
            if (ep.TryGet("ch", out var v) || ep.TryGet("channel", out v) || ep.TryGet("bus", out v))
                ch = v!;
        }
        var provider = CanRegistry.Registry.Resolve(PcanDeviceType.PCANBasic);
        var (devOpt, devCfg) = provider.GetDeviceOptions();
        var (chOpt, chCfg) = provider.GetChannelOptions();
        chCfg.UseChannelName(ch);
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
        return CanBus.Open<PcanBus, PcanBusOptions, PcanBusInitConfigurator>(
            device, (PcanBusOptions)chOpt, (PcanBusInitConfigurator)chCfg);
    }

    /// <summary>
    /// Enumerate available PCAN channels using PCAN-Basic, if present. Fails safe to empty.
    /// ZH: 通过 PCAN-Basic 枚举可用通道；失败则返回空列表。
    /// </summary>
    public static IEnumerable<BusEndpointInfo> Enumerate()
    {
        PcanChannelInformation[] chans;
        try
        {
            var sts = Api.GetAttachedChannels(out chans);
            if (sts != PcanStatus.OK)
                yield break;
        }
        catch
        {
            yield break;
        }
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
