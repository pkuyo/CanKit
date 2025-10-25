using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CanKit.Adapter.ControlCAN.Options;
using CanKit.Adapter.ControlCAN.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;
using CanKit.Core.Endpoints;
using CanKit.Core.Exceptions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ControlCAN;

/// <summary>
/// Endpoint handler for scheme "controlcan".
/// Examples:
///  - controlcan://VCI_USBCAN2?index=0#ch1
///  - controlcan://USBCAN2?index=0#ch1
///  - controlcan://?type=USBCAN2&index=0#ch0
/// </summary>
[CanEndPoint("controlcan", [])]
public static class ControlCanEndpoint
{
    public static PreparedBusContext Prepare(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        var dt = ResolveDeviceType(ep.Path, ep);
        var provider = CanRegistry.Registry.Resolve(dt);

        var (devOpt, devCfg) = provider.GetDeviceOptions();
        uint devIndex = 0;
        if (ep.TryGet("index", out var s) && !string.IsNullOrWhiteSpace(s))
        {
            _ = uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out devIndex);
        }
        TrySetDeviceIndex(devOpt, devIndex);

        int chIndex = 0;
        if (!string.IsNullOrWhiteSpace(ep.Fragment))
        {
            var frag = ep.Fragment!;
            if (frag.StartsWith("ch", StringComparison.OrdinalIgnoreCase))
                frag = frag[2..];
            _ = int.TryParse(frag, NumberStyles.Integer, CultureInfo.InvariantCulture, out chIndex);
        }

        var (chOpt, chCfg) = provider.GetChannelOptions();
        configure?.Invoke(chCfg);
        chCfg.UseChannelIndex(chIndex);

        return new PreparedBusContext(provider, devOpt, devCfg, chOpt, chCfg);
    }

    public static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        var (provider, devOpt, _, chOpt, chCfg) = Prepare(ep, configure);

        var (device, lease) = ControlCanDeviceMultiplexer.Acquire(devOpt.DeviceType, ((ControlCanDeviceOptions)devOpt).DeviceIndex, () =>
        {
            var d = provider.Factory.CreateDevice(devOpt);
            if (d == null)
                throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"Factory '{provider.Factory.GetType().FullName}' returned null device.");
            return d;
        });

        var transceiver = provider.Factory.CreateTransceivers(device.Options, chCfg);
        if (transceiver == null)
            throw new CanFactoryException(CanKitErrorCode.TransceiverMismatch, $"Factory '{provider.Factory.GetType().FullName}' returned null transceiver.");

        var channel = provider.Factory.CreateBus(device, chOpt, transceiver, provider);
        if (channel == null)
            throw new CanBusCreationException($"Factory '{provider.Factory.GetType().FullName}' returned null channel.");

        if (channel is IBusOwnership own)
            own.AttachOwner(lease);
        return channel;
    }

    private static DeviceType ResolveDeviceType(string path, CanEndpoint ep)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (DeviceType.TryFromId(path, out var v)) return v;
            string normalized = path.Replace('-', '_');
            if (!normalized.StartsWith("VCI_", StringComparison.OrdinalIgnoreCase))
                normalized = "VCI_" + normalized;
            string candidate = "ControlCAN." + normalized;
            if (DeviceType.TryFromId(candidate, out v)) return v;
        }

        if (ep.TryGet("type", out var t) && !string.IsNullOrWhiteSpace(t))
        {
            return ResolveDeviceType(t!, ep);
        }

        // default to USBCAN2
        return ControlCanDeviceType.VCI_USBCAN2;
    }

    private static void TrySetDeviceIndex(IDeviceOptions devOpt, uint index)
    {
        try { ((ControlCanDeviceOptions)devOpt).DeviceIndex = index; }
        catch { }
    }
}
