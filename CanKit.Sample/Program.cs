#define LISTEN
#define SEND

using System;
using System.Text;
using System.Threading;
using CanKit.Core;
using CanKit.Core.Definitions;
using CanKit.Core.Endpoints;

namespace CanKit.Sample
{
    internal class CanKitSample
    {

        private static readonly string[] _listenEndpoints =
        [
            "zlg://ZCAN_USBCANFD_200U?index=0#ch0",
            "pcan://PCAN_USBBUS1",
            "kvaser://0",
        ];

        private static readonly string[] _sendEndPoints =
        [
            "zlg://ZCAN_USBCANFD_200U?index=0#ch0",
            "pcan://PCAN_USBBUS1",
            "kvaser://0"
        ];

        static void Main(string[] args)
        {
            var re = BusEndpointEntry.Enumerate("pcan", "kvaser");
            foreach (var info in re)
            {
                Console.WriteLine(info.Endpoint);
            }


#if LISTEN
            using var listenChannel = CanBus.Open(_listenEndpoints[2], cfg =>
                cfg.Baud(500_000)
                    .InternalRes(true)
                    .SetProtocolMode(CanProtocolMode.Can20));
            /*
            var sframe1 = new CanTransmitData(new CanClassicFrame(0x1824080F,
                new ReadOnlyMemory<byte>([0xAA, 0xBB, 0xCC, 0xFF]), true));
            var sframe2 = new CanTransmitData(new CanClassicFrame(0x18240801,
                new ReadOnlyMemory<byte>([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xEE]), true));

            using var speriodicTx1 = listenChannel.TransmitPeriodic(sframe1,
                new PeriodicTxOptions(TimeSpan.FromMilliseconds(200), 100));
            using var speriodicTx2 = listenChannel.TransmitPeriodic(sframe2,
               new PeriodicTxOptions(TimeSpan.FromMilliseconds(200), 100));
            */
            // 2. Subscribe RX and error events

            listenChannel.FrameReceived += (sender, data) =>
            {
                Console.Write(
                    $"[{data.SystemTimestamp}] [{data.RecvTimestamp / 1000f}ms] 0x{data.CanFrame.ID:X}, {data.CanFrame.Dlc}");
                foreach (var by in data.CanFrame.Data.Span)
                    Console.Write($" {by:X2}");
                Console.WriteLine();
            };


            listenChannel.ErrorOccurred += (sender, frame) =>
            {
                StringBuilder b = new StringBuilder("[Channel Listen] ");
                b.Append(
                    $"[{frame.SystemTimestamp}] Error Kind: {frame.Type}, Direction:{frame.Direction}, ControllerStatus:{frame.ControllerStatus}, ProtocolViolation:{frame.ProtocolViolation} ");
                if (frame.ProtocolViolationLocation != FrameErrorLocation.Unrecognized)
                    b.Append($"Location:{frame.ProtocolViolationLocation}, ");
                if (frame.ErrorCounters != null)
                    b.Append($"ErrorCounters:{frame.ErrorCounters}, ");
                if (frame.ArbitrationLostBit != null)
                    b.Append($"Arbitration Lost:{frame.ArbitrationLostBit}, ");
                Console.WriteLine(b);
            };

#endif

#if SEND

            // 1. Open two buses via endpoint
            using var sendChannel = CanBus.Open(_sendEndPoints[0], cfg =>
                cfg.Baud(500_000)
                    .InternalRes(true)
                    .SoftwareFeaturesFallBack(CanFeature.CyclicTx)
                    .SetProtocolMode(CanProtocolMode.Can20));

            var frame1 = new CanTransmitData(new CanClassicFrame(0x1824080F,
                new ReadOnlyMemory<byte>([0xAA, 0xBB, 0xCC, 0xDD]), true));
            var frame2 = new CanTransmitData(new CanClassicFrame(0x18240801,
                new ReadOnlyMemory<byte>([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF]), true));

            using var periodicTx1 = sendChannel.TransmitPeriodic(frame1,
                new PeriodicTxOptions(TimeSpan.FromMilliseconds(1), 100));
            using var periodicTx2 = sendChannel.TransmitPeriodic(frame2,
                new PeriodicTxOptions(TimeSpan.FromMilliseconds(1), 100));
            sendChannel.ErrorOccurred += (sender, frame) =>
            {
                StringBuilder b = new StringBuilder("[Channel Send] ");
                b.Append(
                    $"[{frame.SystemTimestamp}] Error Kind: {frame.Type}, Direction:{frame.Direction}, ControllerStatus:{frame.ControllerStatus}, ProtocolViolation:{frame.ProtocolViolation} ");
                if (frame.ProtocolViolationLocation != FrameErrorLocation.Unrecognized)
                    b.Append($"Location:{frame.ProtocolViolationLocation}, ");
                if (frame.ErrorCounters != null)
                    b.Append($"ErrorCounters:{frame.ErrorCounters}, ");
                if (frame.ArbitrationLostBit != null)
                    b.Append($"Arbitration Lost:{frame.ArbitrationLostBit}, ");
                Console.WriteLine(b);
            };

#endif
            Thread.Sleep(50000);
        }
    }
}
