using System.Collections.Generic;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Abstractions;

public interface ICanFactory
{
    ICanDevice CreateDevice(IDeviceOptions options);
    
    ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, IEnumerable<ITransceiver> transceivers);
    
    bool Support(DeviceType deviceType);
    
    string Name { get; }
}