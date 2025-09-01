using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG;
using Pkuyo.CanKit.ZLG.Utils;

namespace Pkuyo.CanKit.Net.Test
{
    class Program
    {
   
        static void Main(string[] args)
        {
            using var can = ZlgDeviceType.ZCAN_USBCAN2
                .Open(cfg => 
                    cfg.DeviceIndex(0)
                    .TxTimeOut(50)
                    .MergeReceive(false));
            
            var channel = can.CreateChannel(0, cfg => cfg.Baud(500_0000));
            channel.Transmit(new CanTransmitData()
            {
                canFrame = new CanClassicFrame(0X123456,[0xAA,0xBB,0xCC,0xDD])
            });
        }
    }
}
