using ZlgCAN.Net.Core;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Tests
{
    class Program
    {
   
        static void Main(string[] args)
        {

            var provider = CanCore.Registry.Resolve(ZlgDeviceType.ZCAN_USBCAN2);
            using var device = CanCore.ZLGFactory.CreateDevice(provider.CreateDeviceOptions());
            
            if (!device.OpenDevice())
                return;

            using var channel = CanCore.ZLGFactory.CreateChannel(device, 
                provider.CreateChannelInitOptions(0),
                provider.CreateChannelRuntimeOptions(0));
            
            channel.Start();

            channel.Transmit(new CanClassicFrame(0x1034563,[0xAA,0xBB,0xCC,0xDD,0xEE,0xFF]));
            
            channel.Reset();
            
        }
    }
}
