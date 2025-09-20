using System;
using System.Threading;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Sample
{
    internal class CanKitSample
    {
        static void Main(string[] args)
        {
            // 1. Open two buses via endpoint (equivalent to ch0/ch1)
            var sendChannel = CanBus.Open("zlg://ZCAN_USBCAN2?index=0#ch0", cfg =>
                cfg.Baud(500_000)
                    .SetProtocolMode(CanProtocolMode.Can20));

            var listenChannel = CanBus.Open("zlg://ZCAN_USBCAN2?index=0#ch1", cfg =>
                cfg.Baud(500_000)
                    .AccMask(0X78, 0xFFFFFF87, CanFilterIDType.Standard)
                    .SetWorkMode(ChannelWorkMode.ListenOnly)
                    .SetProtocolMode(CanProtocolMode.Can20));

            // 2. Subscribe RX and error events
            listenChannel.FrameReceived += (sender, data) =>
            {
                Console.Write($"[{data.SystemTimestamp}] [{data.recvTimestamp / 1000f}ms] 0x{data.CanFrame.ID:X}, {data.CanFrame.Dlc}");
                foreach (var by in data.CanFrame.Data.Span)
                    Console.Write($" {by:X2}");
                Console.WriteLine();
            };

            listenChannel.ErrorOccurred += (sender, frame) =>
            {
                Console.WriteLine(
                    $"[{frame.SystemTimestamp}] Error Kind: {frame.Kind}, Direction:{frame.Direction}");
            };

            // 3. Open buses and send sample data
            sendChannel.Open();
            listenChannel.Open();

            for (int i = 0; i < 50; i++)
            {
                sendChannel.Transmit(new[]
                {
                    new CanTransmitData(new CanClassicFrame(0x1824080F, new ReadOnlyMemory<byte>(new byte[]{0xAA,0xBB,0xCC,0xDD}), true)),
                    new CanTransmitData(new CanClassicFrame(0x18240801, new ReadOnlyMemory<byte>(new byte[]{0xAA,0xBB,0xCC,0xDD,0xEE,0xFF}), true))
                });
                Thread.Sleep(200);
            }

            sendChannel.Dispose();
            listenChannel.Dispose();
        }
    }
}
