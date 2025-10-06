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
            var logger = new FrameLogger("can_log.txt");
            var sampleCounter = 0;
            const int sampleEvery = 1000;

            foreach (var info in re)
            {
                Console.WriteLine(info.Endpoint);
            }


#if LISTEN
            using var listenChannel = CanBus.Open(_listenEndpoints[1], cfg =>
                cfg.Baud(500_000)
                    .InternalRes(true)
                    .SetProtocolMode(CanProtocolMode.Can20));

            listenChannel.FrameReceived += (sender, data) =>
            {
                var fr = new FrameRecord(
                    sysTs: data.SystemTimestamp,
                    rxMs: data.ReceiveTimestamp.TotalMilliseconds,
                    id: data.CanFrame.ID,
                    dlc: data.CanFrame.Dlc,
                    data: data.CanFrame.Data.Span);

                logger.TryEnqueue(fr);

                if (Interlocked.Increment(ref sampleCounter) % sampleEvery == 0)
                {
                    var sb = new StringBuilder(128);
                    fr.AppendTextLine(sb);
                    Console.Write(sb); // 仅每1000帧一次
                }
            };


            listenChannel.ErrorOccurred += (sender, frame) =>
            {
                StringBuilder b = new StringBuilder("[Channel Listen] ");
                b.Append(
                    $"[{frame.SystemTimestamp}] Error Type: {frame.Type}, Direction:{frame.Direction}, ControllerStatus:{frame.ControllerStatus}, ProtocolViolation:{frame.ProtocolViolation} ");
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
                new PeriodicTxOptions(TimeSpan.FromMilliseconds(1)));
            using var periodicTx2 = sendChannel.TransmitPeriodic(frame2,
                new PeriodicTxOptions(TimeSpan.FromMilliseconds(1)));
            sendChannel.ErrorOccurred += (sender, frame) =>
            {
                StringBuilder b = new StringBuilder("[Channel Send] ");
                b.Append(
                    $"[{frame.SystemTimestamp}] Error Type: {frame.Type}, Direction:{frame.Direction}, ControllerStatus:{frame.ControllerStatus}, ProtocolViolation:{frame.ProtocolViolation} ");
                if (frame.ProtocolViolationLocation != FrameErrorLocation.Unrecognized)
                    b.Append($"Location:{frame.ProtocolViolationLocation}, ");
                if (frame.ErrorCounters != null)
                    b.Append($"ErrorCounters:{frame.ErrorCounters}, ");
                if (frame.ArbitrationLostBit != null)
                    b.Append($"Arbitration Lost:{frame.ArbitrationLostBit}, ");
                Console.WriteLine(b);
            };
            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(60000);
                var frame3 = new CanTransmitData(new CanClassicFrame(0x22222222,
                    new ReadOnlyMemory<byte>([0xDD, 0xCC, 0xBB, 0xAA]), true));
                var frame4 = new CanTransmitData(new CanClassicFrame(0x3333333,
                    new ReadOnlyMemory<byte>([0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, 0x11, 0x22]), true));
                using var periodicTx3 = sendChannel.TransmitPeriodic(frame3,
                    new PeriodicTxOptions(TimeSpan.FromMilliseconds(1)));
                using var periodicTx4 = sendChannel.TransmitPeriodic(frame4,
                    new PeriodicTxOptions(TimeSpan.FromMilliseconds(1)));
                Thread.Sleep(60000);
            }
#endif

        }
    }
}
