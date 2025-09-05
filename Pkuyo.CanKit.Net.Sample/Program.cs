using System;
using System.Threading;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG;
using Pkuyo.CanKit.ZLG.Options;

namespace Pkuyo.CanKit.Net.Sample
{
    class Program
    {

        static void Main(string[] args)
        {
            using var can = ZlgDeviceType.ZCAN_USBCAN2
                .Open(cfg =>
                    cfg .DeviceIndex(0)
                        .TxTimeOut(50));

            if (!can.IsDeviceOpen)
            {
                Console.WriteLine("Open Device Failed");
                return;
            }

            var sendChannel = can.CreateChannel(0, cfg =>
                cfg.Baud(500_000)
                    .SetProtocolMode(CanProtocolMode.Can20));

            var listenChannel = can.CreateChannel(1, cfg =>
                cfg.Baud(500_000)
                    .AccMask(0X78,0xFFFFFF87)
                    .SetWorkMode(ChannelWorkMode.ListenOnly)
                    .SetMaskFilterType(ZlgChannelOptions.MaskFilterType.Single)
                    .SetProtocolMode(CanProtocolMode.Can20));
            
          
            listenChannel.FrameReceived += (sender, data) =>
            {
                Console.Write($"[{data.SystemTimestamp}] [{data.recvTimestamp/1000f}ms] 0x{data.canFrame.ID:X}, {data.canFrame.Dlc}");
                foreach(var by in data.canFrame.Data.Span)
                    Console.Write($" {by:X2}");
                Console.WriteLine();
            };

            listenChannel.ErrorOccurred += (sender, frame) =>
            {
                Console.WriteLine("Error Occurred!");
            };
            
            sendChannel.Open();
            listenChannel.Open();
            
            for (int i = 0; i < 500; i++)
            {
                sendChannel.Transmit(
                    new CanClassicFrame(0x1824080F, new ReadOnlyMemory<byte>([0xAA, 0xBB, 0xCC, 0xDD]), true),
                    new CanClassicFrame(0x18240801, new ReadOnlyMemory<byte>([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF]),
                        true)
                );
                Thread.Sleep(400);

            }
        }
    }
}
