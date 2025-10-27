using System;
using System.Collections.Generic;
using System.Linq;
using CanKit.Adapter.Vector.Native;

namespace CanKit.Adapter.Vector.Utils;

internal static class VectorDriver
{
    private static readonly object Gate = new();
    private static readonly DriverRefCounter RefCounter = new(OnFirstAcquire, OnLastRelease);
    private static readonly Dictionary<int, VectorChannelInfo> ChannelCache = new();

    public static IDisposable Acquire() => RefCounter.Acquire();

    public static bool TryGetChannelInfo(int channelIndex, out VectorChannelInfo info)
    {
        lock (Gate)
        {
            if (ChannelCache.TryGetValue(channelIndex, out info!))
                return true;
        }

        using (Acquire())
        {
            lock (Gate)
            {
                RefreshChannelCache();
                return ChannelCache.TryGetValue(channelIndex, out info!);
            }
        }
    }

    private static void OnFirstAcquire()
    {
        VectorErr.ThrowIfError(VxlApi.xlOpenDriver(), "xlOpenDriver");
    }

    private static void OnLastRelease()
    {
        var status = VxlApi.xlCloseDriver();
        if (status != VxlApi.XL_SUCCESS)
            VectorErr.LogNonFatal(status, "xlCloseDriver", "Vector");
    }

    private static void RefreshChannelCache()
    {
        var cfg = CreateDriverConfig();
        VectorErr.ThrowIfError(VxlApi.xlGetDriverConfig(ref cfg), "xlGetDriverConfig");

        ChannelCache.Clear();

        if (cfg.Channel.Length == 0)
            return;

        var count = (int)Math.Min(cfg.ChannelCount, (uint)cfg.Channel.Length);
        for (int i = 0; i < count; i++)
        {
            ref var ch = ref cfg.Channel[i];
            if (ch.ChannelMask == 0)
                continue;

            var supportsClassic = (ch.ChannelBusCapabilities & VxlApi.XL_BUS_COMPATIBLE_CAN) != 0;
            if (!supportsClassic)
                continue;

            var supportsFd = (ch.ChannelCapabilities & (VxlApi.XL_CHANNEL_FLAG_CANFD_ISO_SUPPORT | VxlApi.XL_CHANNEL_FLAG_CANFD_BOSCH_SUPPORT)) != 0;

            var name = string.IsNullOrWhiteSpace(ch.Name) ? $"VectorChannel{ch.ChannelIndex}" : ch.Name.Trim();
            var transceiver = string.IsNullOrWhiteSpace(ch.TransceiverName) ? null : ch.TransceiverName.Trim();

            var info = new VectorChannelInfo(
                ch.ChannelIndex,
                ch.ChannelMask,
                name,
                ch.HwType,
                ch.HwIndex,
                ch.HwChannel,
                transceiver,
                ch.ChannelCapabilities,
                ch.ChannelBusCapabilities,
                ch.ConnectedBusType,
                supportsFd);

            ChannelCache[ch.ChannelIndex] = info;
        }
    }

    private static VxlApi.XLdriverConfig CreateDriverConfig()
    {
        var cfg = new VxlApi.XLdriverConfig
        {
            Reserved = new uint[10],
            Channel = new VxlApi.XLchannelConfig[64]
        };

        if (cfg.Channel != null)
        {
            for (int i = 0; i < cfg.Channel.Length; i++)
            {
                cfg.Channel[i].RawData = new uint[10];
                cfg.Channel[i].Reserved = new uint[3];
                cfg.Channel[i].BusParams = new VxlApi.XLbusParams { Data = new byte[28], BusType = 0 };
            }
        }

        return cfg;
    }
}

