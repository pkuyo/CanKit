using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Abstractions;

public interface ICanFactory
{
    ICanDevice CreateDevice(IDeviceOptions options);
    
    ICanChannel CreateChannel(ICanDevice device, IChannelInitOptions options, IChannelRuntimeOptions runtimeOptions);
    
    bool Support(DeviceType deviceType);
    
    string Name { get; }
}