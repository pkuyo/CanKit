using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.ZLG.Options;

public sealed class ZlgDeviceInitOptionsConfigurator
    : DeviceInitOptionsConfigurator<ZlgDeviceOptions, ZlgDeviceInitOptionsConfigurator>
{
    public ZlgDeviceInitOptionsConfigurator DeviceIndex(uint deviceIndex)
    {
        Options.DeviceIndex = deviceIndex;
        return this;
    }
}

public sealed class ZlgDeviceRTOptionsConfigurator
    : DeviceRTOptionsConfigurator<ZlgDeviceOptions>
{
    public uint DeviceIndex => Options.DeviceIndex;
    
}

public sealed class ZlgChannelInitConfigurator 
    : ChannelInitOptionsConfigurator<ZlgChannelOptions,ZlgChannelInitConfigurator>
{

}

public sealed class ZlgChannelRTConfigurator 
    : ChannelRTOptionsConfigurator<ZlgChannelOptions>
{

}