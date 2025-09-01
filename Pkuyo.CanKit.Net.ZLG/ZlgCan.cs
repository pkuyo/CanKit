using System;
using Pkuyo.CanKit.Net;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Options;

namespace Pkuyo.CanKit.ZLG
{
    public static class ZlgCan
    {
        public static CanSession<ZlgCanDevice,ZlgCanChannel> Open(this ZlgDeviceType deviceType,
            Action<ZlgDeviceInitOptionsConfigurator> configure = null)
        {
            return Can.Open<ZlgCanDevice,ZlgCanChannel,ZlgDeviceOptions,ZlgDeviceInitOptionsConfigurator>(
                deviceType, configure,(device, provider) => 
                    new CanSession<ZlgCanDevice,ZlgCanChannel>(device, provider));
        }

        public static ZlgCanChannel CreateChannel(this CanSession<ZlgCanDevice,ZlgCanChannel> session,
            int channelIndex,
            Action<ZlgChannelInitConfigurator> configure = null)
        {
            return session.CreateChannel<ZlgChannelOptions,ZlgChannelInitConfigurator>(
                channelIndex, configure);
        }
    }


}