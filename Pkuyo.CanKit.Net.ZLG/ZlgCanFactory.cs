using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Native;
using Pkuyo.CanKit.ZLG.Transceivers;

namespace Pkuyo.CanKit.ZLG;

[CanFactory("Zlg")]
public class ZlgCanFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        return new ZlgCanDevice(options);
    }

    public ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, ITransceiver transceiver)
    {
        
        if(device is not ZlgCanDevice zlgCanDevice)
            throw new Exception(); //TODO: 异常处理
        
        return new ZlgCanChannel(zlgCanDevice, options, transceiver);
    }

    public bool Support(DeviceType deviceType)
    {
        return deviceType.Id.StartsWith("ZCAN");
    }
    
    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator configurator,
        IChannelInitOptionsConfigurator channelOptions)
    {
        if (configurator.Provider is not ZlgCanProvider provider)
            throw new Exception(); //TODO: 异常处理
        
        if (channelOptions.ProtocolMode == CanProtocolMode.Merged && provider.EnableMerge)
            return new ZlgMergeTransceiver();
            
        if (channelOptions.ProtocolMode == CanProtocolMode.Can20 
            && (uint)(provider.Features & CanFeature.CanClassic) != 0U)
            return new ZlgCanClassicTransceiver();
            
        if (channelOptions.ProtocolMode == CanProtocolMode.CanFd
            && (uint)(provider.Features & CanFeature.CanFd) != 0U)
            return new ZlgCanFdTransceiver();
            
        throw new NotSupportedException();
    }
    
}