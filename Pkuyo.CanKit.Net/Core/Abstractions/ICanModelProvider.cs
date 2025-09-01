using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{
    
    public interface ICanModelProvider
    {
        DeviceType DeviceType { get; }
        
        CanFeature Features { get; }

        ICanFactory Factory { get; }
        
        (IDeviceOptions,IDeviceInitOptionsConfigurator<IDeviceOptions>) GetDeviceOptions();
        
        (IChannelOptions,IChannelInitOptionsConfigurator<IChannelOptions>) GetChannelOptions(int channelIndex);
        
        IEnumerable<ITransceiver> CreateTransceivers();
        
    }
}