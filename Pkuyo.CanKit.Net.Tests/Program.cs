using System;
using System.Linq;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG;
using Pkuyo.CanKit.ZLG.Native;
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
                    .TxTimeOut(50));

            if (!can.IsDeviceOpen)
            {
                Console.WriteLine("Open Device Failed");
                return;
            }
            
            var sendChannel = can.CreateChannel(0, cfg => 
                cfg.Baud(50_0000)
                .SetProtocolMode(CanProtocolMode.Can20));
            
            var listenChannel = can.CreateChannel(1, cfg => 
                cfg.Baud(50_0000)
                    .SetProtocolMode(CanProtocolMode.Can20));
            
            sendChannel.Start();
            listenChannel.Start();
            
            sendChannel.Transmit(
                new CanClassicFrame(0x18240801,new ReadOnlyMemory<byte>([0xAA,0xBB,0xCC,0xDD]),true),
            new CanClassicFrame(0x18240801,new ReadOnlyMemory<byte>([0xAA,0xBB,0xCC,0xDD,0xEE,0xFF]),true)
            );

            var receviceDatas = listenChannel.Receive(CanFrameType.CanClassic, 2, -1).ToArray();
            Console.WriteLine($"Receviced {receviceDatas.Length} datas");
            foreach (var data in receviceDatas)
            {
                Console.Write($"[{data.SystemTimestamp}] [{data.recvTimestamp/1000f}ms] 0x{data.canFrame.ID:X}, {data.canFrame.Dlc}");
                foreach(var by in data.canFrame.Data.Span)
                    Console.Write($" {by:X2}");
                Console.WriteLine();
            }
        }
    }
}
