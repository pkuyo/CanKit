using System;
using Pkuyo.CanKit.Net;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Options;

namespace Pkuyo.CanKit.ZLG
{
    public static class ZlgCan
    {
        public static ZlgCanSession Open(this ZlgDeviceType deviceType,
            Action<ZlgDeviceInitOptionsConfigurator> configure = null)
        {
            return (ZlgCanSession)Can.Open<ZlgCanDevice,ZlgCanChannel,ZlgDeviceOptions,ZlgDeviceInitOptionsConfigurator>(
                deviceType, configure,(device, provider) => 
                    new ZlgCanSession(device, provider));
        }
        
    }

    public class ZlgCanSession(ZlgCanDevice device, ICanModelProvider provider) : CanSession<ZlgCanDevice, ZlgCanChannel>(device, provider)
    {
        public ZlgCanChannel CreateChannel(int index, Action<ZlgChannelInitConfigurator> configure = null)
        {
            return CreateChannel<ZlgChannelOptions,ZlgChannelInitConfigurator>(index, configure);
        }

    }


}