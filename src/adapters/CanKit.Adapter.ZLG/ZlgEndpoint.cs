using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Adapter.ZLG.Options;
using CanKit.Core.Definitions;
using CanKit.Core.Endpoints;
using CanKit.Core.Exceptions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ZLG;

/// <summary>
/// Endpoint handler for scheme "zlg" (ZLG Endpoint 处理器，支持同设备多通道)。
/// </summary>
internal static class ZlgEndpoint
{
    public static PreparedBusContext Prepare(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        var dt = ResolveDeviceType(ep.Path);
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
                frag = frag.Substring(2);
            _ = int.TryParse(frag, NumberStyles.Integer, CultureInfo.InvariantCulture, out chIndex);
        }

        var (chOpt, chCfg) = provider.GetChannelOptions();
        configure?.Invoke(chCfg);
        chCfg.UseChannelIndex(chIndex);

        return new PreparedBusContext(provider, devOpt, devCfg, chOpt, chCfg);
    }

    public static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        // 路径匹配 DeviceType.Id 或其去前缀的尾部，例如：
        //   zlg://ZLG.ZCAN_USBCANFD_200U?index=0#ch1
        //   zlg://ZCAN_USBCANFD_200U?index=0#ch1
        //   zlg://USBCANFD-200U?index=0#ch1
        var (provider, devOpt, _, chOpt, chCfg) = Prepare(ep, configure);

        // 获取设备租约（同设备的多个通道共享）
        var (device, lease) = ZlgDeviceMultiplexer.Acquire(devOpt.DeviceType, ((ZlgDeviceOptions)devOpt).DeviceIndex, () =>
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

    private static DeviceType ResolveDeviceType(string path)
    {
        if (DeviceType.TryFromId(path, out var v)) return v;
        string normalized = path.Replace('-', '_');
        if (!normalized.StartsWith("ZCAN_", StringComparison.OrdinalIgnoreCase))
            normalized = "ZCAN_" + normalized;
        string candidate = "ZLG." + normalized;
        if (DeviceType.TryFromId(candidate, out v)) return v;

        // fallback: suffix match over all types
        var all = DeviceType.List();
        var m = all.FirstOrDefault(t => t.Id.EndsWith(normalized, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(t => t.Id.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0);
        if (m != null) return m;
        throw new KeyNotFoundException($"Unknown ZLG device type path '{path}'.");
    }

    private static void TrySetDeviceIndex(IDeviceOptions devOpt, uint index)
    {
        try
        {
            ((ZlgDeviceOptions)devOpt).DeviceIndex = index;
        }
        catch
        {
            // best effort; ignore
        }
    }
}
