using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Native;

namespace Pkuyo.CanKit.ZLG;

[CanFactory("Zlg")]
public class ZlgCanFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        return new ZlgCanDevice(options);
    }

    public ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, IEnumerable<ITransceiver> transceivers)
    {
        
        if(device is not ZlgCanDevice zlgCanDevice)
            throw new Exception(); //TODO: 异常处理
        
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
        var handle = ZLGCAN.ZCAN_InitCAN(zlgCanDevice.NativeHandler, (uint)options.ChannelIndex, ref config);
        if (handle.IsInvalid)
            return null;
        handle.SetDevice(zlgCanDevice.NativeHandler);
        return new ZlgCanChannel(handle, options, transceivers, options.Provider.Features);
    }

    public bool Support(DeviceType deviceType)
    {
        return deviceType.Id.StartsWith("ZCAN");
    }
    
}