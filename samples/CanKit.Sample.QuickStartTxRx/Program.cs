using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Sample.QuickStartTxRx
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // Usage:
            //  QuickStartTxRx --src virtual://alpha/0 [--dst virtual://alpha/1] [--bitrate 500000] [--dbitrate 2000000] [--fd] [--brs] [--count 10] [--ext] [--res 1]
            var tx = GetArg(args, "--src") ?? "virtual://alpha/0";
            var rx = GetArg(args, "--dst");
            bool useFd = HasFlag(args, "--fd");
            bool brs = HasFlag(args, "--brs");
            bool extended = HasFlag(args, "--ext");
            uint bitrate = ParseUInt(GetArg(args, "--bitrate"), 500_000);
            uint dbitrate = ParseUInt(GetArg(args, "--dbitrate"), 2_000_000);
            int count = (int)ParseUInt(GetArg(args, "--count"), 5);
            bool enableRes = (ParseUInt(GetArg(args, "--res"), 1) == 1U);
            using var txBus = CanBus.Open(tx, cfg =>
            {
                if (useFd)
                {
                    cfg.Fd(bitrate, dbitrate).SetProtocolMode(CanProtocolMode.CanFd).InternalRes(enableRes);
                }
                else
                {
                    cfg.Baud(bitrate).SetProtocolMode(CanProtocolMode.Can20).InternalRes(enableRes);
                }
                if (rx == null)
                {
                    cfg.SetWorkMode(ChannelWorkMode.Echo); // echo so we can see our own TX
                }
            });
            var rxBus = txBus;
            try
            {


                if (rx != null)
                {
                    rxBus = CanBus.Open(rx, cfg =>
                    {
                        if (useFd)
                        {
                            cfg.Fd(bitrate, dbitrate).SetProtocolMode(CanProtocolMode.CanFd)
                                .InternalRes(enableRes);
                        }
                        else
                        {
                            cfg.Baud(bitrate).SetProtocolMode(CanProtocolMode.Can20).InternalRes(enableRes);
                        }
                    });
                }

                using var _ = SubscribeLogging(rxBus);

                Console.WriteLine($"Opened: {tx} ({(useFd ? "CAN-FD" : "Classic")})");

                // Compose a demo frame
                var payload = new byte[] { 0x11, 0x22, 0x33, 0x44 };
                var id = extended ? 0x18DAF110u : 0x123u;

                for (int i = 0; i < count; i++)
                {
                    var data = payload.AsMemory();
                    ICanFrame f = useFd
                        ? new CanFdFrame(id, data, BRS: brs, ESI: false) { IsExtendedFrame = extended }
                        : new CanClassicFrame(id, data, isExtendedFrame: extended);

                    var sent = await txBus.TransmitAsync(new[] { new CanTransmitData(f) });
                    Console.WriteLine($"TX {i + 1}/{count}: id=0x{id:X} dlc={f.Dlc} kind={f.FrameKind} sent={sent}");

                    await Task.Delay(100);
                }
                Console.WriteLine("Done. Press Enter to exit.");
                Console.ReadLine();
            }
            finally
            {
                if (rx != null) rxBus.Dispose();
            }

            return 0;
        }

        private static IDisposable SubscribeLogging(ICanBus bus)
        {
            EventHandler<CanReceiveData> onRx = (_, e) =>
            {
                var fr = e.CanFrame;
                var sb = new StringBuilder();
                sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("] ");
                sb.Append("RX ");
                sb.Append(fr.FrameKind == CanFrameType.CanFd ? "FD" : "CL");
                sb.Append(' ');
                sb.Append((fr.RawID & 0x8000_0000) != 0 ? "ext" : "std");
                sb.Append(" id=0x").Append(fr.ID.ToString("X"));
                sb.Append(" dlc=").Append(fr.Dlc);
                sb.Append(" data=").Append(ToHex(fr.Data.Span));
                Console.WriteLine(sb.ToString());
            };
            bus.FrameReceived += onRx;
            return new ActionOnDispose(() => bus.FrameReceived -= onRx);
        }

        private sealed class ActionOnDispose : IDisposable
        { private readonly Action _a; public ActionOnDispose(Action a) => _a = a; public void Dispose() { try { _a(); } catch { } } }

        private static string? GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            foreach (var a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static uint ParseUInt(string? s, uint def) => uint.TryParse(s, out var v) ? v : def;

        private static string ToHex(ReadOnlySpan<byte> span)
        {
            if (span.Length == 0) return "";
            var arr = ArrayPool<char>.Shared.Rent(span.Length * 2);
            try
            {
                for (int i = 0; i < span.Length; i++)
                {
                    var b = span[i];
                    arr[2 * i] = GetHex((byte)(b >> 4));
                    arr[2 * i + 1] = GetHex((byte)(b & 0xF));
                }
                return new string(arr, 0, span.Length * 2);
            }
            finally { ArrayPool<char>.Shared.Return(arr); }
        }
        private static char GetHex(byte v) => (char)(v < 10 ? ('0' + v) : ('A' + (v - 10)));
    }
}

