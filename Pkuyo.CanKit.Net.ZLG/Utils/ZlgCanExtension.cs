using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Internal;
using Pkuyo.CanKit.ZLG.Options;

namespace Pkuyo.CanKit.ZLG.Utils
{
    public static class ZlgCanExtension
    {
        public static ChannelInitOptionsConfigurator<ZlgChannelOptions> SerialId(
            this ChannelInitOptionsConfigurator<ZlgChannelOptions> cfg, string serialId)
        {
            return cfg;
        }
        public static DeviceInitOptionsConfigurator<ZlgDeviceOptions> DeviceIndex(
            this DeviceInitOptionsConfigurator<ZlgDeviceOptions> cfg, uint deviceIndex)
        {
#pragma warning disable 0618        
            ((IOptionsAccessor<ZlgDeviceOptions>)cfg).Options.DeviceIndex = deviceIndex;
#pragma warning restore 0618
            return cfg;
        }
    }
}