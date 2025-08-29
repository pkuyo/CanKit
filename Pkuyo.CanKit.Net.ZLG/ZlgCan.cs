using System;
using Pkuyo.CanKit.Net;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Options;

namespace Pkuyo.CanKit.ZLG
{
    public static class ZlgCan
    {
        public static ZlgCanSession Open(DeviceType deviceType, Action<DeviceInitOptionsConfigurator<ZlgDeviceOptions>> configure = null)
        {
            return (ZlgCanSession)Can.Open(deviceType, configure,((device, provider) => new ZlgCanSession(device, provider)));
        }
        
        public static CanChannel CreateChannel(this ZlgCanSession session, 
            int channelIndex, 
            Action<ChannelInitOptionsConfigurator<ZlgChannelOptions>> configure = null)
            => session.CreateChannel(channelIndex, configure);
    }

    public class ZlgCanSession(ICanDevice device, ICanModelProvider provider) : CanSession(device, provider);
}