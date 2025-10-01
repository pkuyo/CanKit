using System;
using System.Threading;
using CanKit.Core;
using CanKit.Core.Definitions;
using CanKit.Core.Endpoints;

namespace CanKit.Sample
{
    internal class CanKitSample
    {
        static void Main(string[] args)
        {
            var re = BusEndpointEntry.Enumerate("pcan","kvaser");
            foreach (var info in re)
            {
                Console.WriteLine(info.Endpoint);
            }
            // 1. Open two buses via endpoint
            using var sendChannel = CanBus.Open("zlg://ZCAN_USBCANFD_200U?index=0#ch1", cfg =>
                cfg.Baud(500_000)
                    .InternalRes(true)
                    .SetProtocolMode(CanProtocolMode.Can20));

            using var listenChannel = CanBus.Open("zlg://ZCAN_USBCANFD_200U?index=0#ch0", cfg =>
                cfg.Baud(500_000)
                    .InternalRes(true)
                    .SetProtocolMode(CanProtocolMode.Can20));
            // 2. Subscribe RX and error events
            listenChannel.FrameReceived += (sender, data) =>
            {
                Console.Write($"[{data.SystemTimestamp}] [{data.RecvTimestamp / 1000f}ms] 0x{data.CanFrame.ID:X}, {data.CanFrame.Dlc}");
                foreach (var by in data.CanFrame.Data.Span)
                    Console.Write($" {by:X2}");
                Console.WriteLine();
            };

            listenChannel.ErrorOccurred += (sender, frame) =>
            {
                Console.WriteLine(
                    $"[{frame.SystemTimestamp}] Error Kind: {frame.Kind}, Direction:{frame.Direction}");
            };

            var frame1 = new CanTransmitData(new CanClassicFrame(0x1824080F, new ReadOnlyMemory<byte>([0xAA, 0xBB, 0xCC, 0xDD]), true));
            var frame2 = new CanTransmitData(new CanClassicFrame(0x18240801, new ReadOnlyMemory<byte>([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF]), true));

            using var periodicTx1 = sendChannel.TransmitPeriodic(frame1, new PeriodicTxOptions(TimeSpan.FromMilliseconds(200), 100));
            using var periodicTx2 = sendChannel.TransmitPeriodic(frame2, new PeriodicTxOptions(TimeSpan.FromMilliseconds(200), 100));

            Thread.Sleep(5000);
        }
    }
}
