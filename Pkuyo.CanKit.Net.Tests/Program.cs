using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG;
using Pkuyo.CanKit.ZLG.Utils;

namespace Pkuyo.CanKit.Net.Test
{
    class Program
    {
   
        static void Main(string[] args)
        {
            using var can = ZlgCan.Open(ZlgDeviceType.ZCAN_USBCAN2,cfg =>
                cfg.TxTimeOut(50)
                    .MergeReceive(true));

            var channel = can.CreateChannel(0, cfg => cfg.SerialId("Test"));
            
            channel.Transmit(new CanClassicFrame(0X123456,[0xAA,0xBB,0xCC,0xDD]));
        }
    }
}
