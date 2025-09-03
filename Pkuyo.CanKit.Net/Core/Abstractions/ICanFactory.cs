using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions;

public interface ICanFactory
{
    ICanDevice CreateDevice(IDeviceOptions options);
    
    ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, ITransceiver transceiver);
    
    ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions,
        IChannelInitOptionsConfigurator channelOptions);
    
    bool Support(DeviceType deviceType);
}