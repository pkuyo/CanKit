using ZlgCAN.Net.Core;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Tests
{
    class Program
    {
   
        static void Main(string[] args)
        {
            using var can = Can.Open(ZlgDeviceType.ZCAN_USBCAN2,cfg =>
                cfg.TxTimeOut(50)
                    .MergeReceive(true));

            var channel = can.CreateChannel(0, cfg =>
                cfg.AccMask(0x0, 0x1FFFFFFF)
                    .Classic(500000));
            
            channel.Transmit(new CanClassicFrame(0X123456,[0xAA,0xBB,0xCC,0xDD]));
        }
    }
}
