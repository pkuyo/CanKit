using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Native;

namespace Pkuyo.CanKit.ZLG;

public class ZlgCanFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        return new ZlgCanDevice(options);
    }

    public ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, IEnumerable<ITransceiver> transceivers)
    {
        var zlgProvider = (ZlgCanProvider)options.Provider;
        ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG config = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG
        {
            can_type = zlgProvider.IsFd ? 1U : 0U
        };
        if (zlgProvider.IsFd)
        {
            
        }
        else
        {
            
        }
        var ptr = ZLGCAN.ZCAN_InitCAN(device.NativePtr, (uint)options.ChannelIndex, ref config);
        if (ptr == IntPtr.Zero)
            return null;

        return new ZlgCanChannel(ptr, options, transceivers, options.Provider.Features);
    }

    public bool Support(DeviceType deviceType)
    {
        return deviceType.Id.StartsWith("ZCAN");
    }

    public string Name => "Zlg";
}