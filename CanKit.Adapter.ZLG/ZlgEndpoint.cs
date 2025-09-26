using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;
using CanKit.Core.Endpoints;
using CanKit.Core.Exceptions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ZLG;

/// <summary>
/// Endpoint handler for scheme "zlg" (ZLG Endpoint 处理器，支持同设备多通道)。
/// </summary>
[CanEndPoint("zlg")]
internal static class ZlgEndpoint
{
    private static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        // 路径匹配 DeviceType.Id 或其去前缀的尾部，例如：
        //   zlg://ZLG.ZCAN_USBCANFD_200U?index=0#ch1
        //   zlg://ZCAN_USBCANFD_200U?index=0#ch1
        //   zlg://USBCANFD-200U?index=0#ch1
        var dt = ResolveDeviceType(ep.Path);
        var provider = CanRegistry.Registry.Resolve(dt);

        var (devOpt, devCfg) = provider.GetDeviceOptions();
        // 从查询参数设置设备索引
        uint devIndex = 0;
        if (ep.TryGet("index", out var s) && !string.IsNullOrWhiteSpace(s))
        {
            _ = uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out devIndex);
        }

        // 设备选项可能与 Provider 相关；若可用则尝试写入 index/tx_timeout/merge
        TrySetDeviceIndex(devOpt, devIndex);
        TrySetDeviceTimeout(devOpt, ep);
        TrySetDeviceMerge(devOpt, ep);

        // 从片段解析通道索引（支持 '#chX' 或 '#X'）
        int chIndex = 0;
        if (!string.IsNullOrWhiteSpace(ep.Fragment))
        {
            var frag = ep.Fragment!;
            if (frag.StartsWith("ch", StringComparison.OrdinalIgnoreCase))
                frag = frag.Substring(2);
            _ = int.TryParse(frag, NumberStyles.Integer, CultureInfo.InvariantCulture, out chIndex);
        }

        var (chOpt, chCfg) = provider.GetChannelOptions(chIndex);
        configure?.Invoke(chCfg);

        // 获取设备租约（同设备的多个通道共享）
        var (device, lease) = ZlgDeviceMultiplexer.Acquire(dt, devIndex, () =>
        {
            var d = provider.Factory.CreateDevice(devOpt);
            if (d == null)
                throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"Factory '{provider.Factory.GetType().FullName}' returned null device.");
            d.OpenDevice();
            return d;
        });

        var transceiver = provider.Factory.CreateTransceivers(device.Options, chCfg);
        if (transceiver == null)
            throw new CanFactoryException(CanKitErrorCode.TransceiverMismatch, $"Factory '{provider.Factory.GetType().FullName}' returned null transceiver.");

        var channel = provider.Factory.CreateBus(device, chOpt, transceiver);
        if (channel == null)
            throw new CanBusCreationException($"Factory '{provider.Factory.GetType().FullName}' returned null channel.");

        if (channel is not ICanBus bus)
            throw new CanBusCreationException($"Created channel type '{channel.GetType().FullName}' does not implement ICanBus.");

        if (channel is IBusOwnership own)
            own.AttachOwner(lease);
        return bus;
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
            var prop = devOpt.GetType().GetProperty("DeviceIndex");
            if (prop != null && prop.CanWrite)
            {
                object boxed = Convert.ChangeType(index, prop.PropertyType, CultureInfo.InvariantCulture);
                prop.SetValue(devOpt, boxed);
            }
        }
        catch
        {
            // best effort; ignore
        }
    }

    private static void TrySetDeviceTimeout(IDeviceOptions devOpt, CanEndpoint ep)
    {
        try
        {
            string? s = null;
            if (!(ep.TryGet("tx", out s) || ep.TryGet("tx_timeout", out s) || ep.TryGet("txTimeout", out s)))
                return;
            if (string.IsNullOrWhiteSpace(s)) return;
            if (!uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return;
            var prop = devOpt.GetType().GetProperty("TxTimeOut");
            if (prop != null && prop.CanWrite)
            {
                object boxed = Convert.ChangeType(v, prop.PropertyType, CultureInfo.InvariantCulture);
                prop.SetValue(devOpt, boxed);
            }
        }
        catch { }
    }

    private static void TrySetDeviceMerge(IDeviceOptions devOpt, CanEndpoint ep)
    {
        try
        {
            string? s = null;
            if (!(ep.TryGet("merge", out s) || ep.TryGet("merge_receive", out s) || ep.TryGet("mergeReceive", out s)))
                return;
            if (string.IsNullOrWhiteSpace(s)) return;
            bool v;
            if (s == "1") v = true; else if (s == "0") v = false; else if (!bool.TryParse(s, out v)) return;
            var prop = devOpt.GetType().GetProperty("MergeReceive");
            if (prop != null && prop.CanWrite)
            {
                object boxed = Convert.ChangeType(v, prop.PropertyType, CultureInfo.InvariantCulture);
                prop.SetValue(devOpt, boxed);
            }
        }
        catch { }
    }
}
