using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Sample.PeriodicTx
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // Usage: PeriodicTx --endpoint <ep> [--id 0x123] [--ext] [--fd] [--brs] [--data 11223344] [--period 100] [--count -1] [--res 1]

            #region ParseArgs

            var endpoint = GetArg(args, "--endpoint") ?? "virtual://alpha/1";
            int id = ParseHex(GetArg(args, "--id"), 0x123);
            bool ext = HasFlag(args, "--ext");
            bool fd = HasFlag(args, "--fd");
            bool brs = HasFlag(args, "--brs");
            bool echo = HasFlag(args, "--echo");
            var dataHex = GetArg(args, "--data") ?? "11223344";
            int period = ParseInt(GetArg(args, "--period"), 100);
            int repeat = int.TryParse(GetArg(args, "--count"), out var c) ? c : -1;
            bool enableRes = (ParseInt(GetArg(args, "--res"), 1) == 1);
            var payload = ParseHexBytes(dataHex);

            #endregion

            using var bus = CanBus.Open(endpoint, cfg =>
            {
                cfg.SetProtocolMode(fd ? CanProtocolMode.CanFd : CanProtocolMode.Can20)
                    .InternalRes(enableRes)
                    .SoftwareFeaturesFallBack(CanFeature.CyclicTx)
                    .SetWorkMode(echo ? ChannelWorkMode.Echo : ChannelWorkMode.Normal);
            });

            ICanFrame frame = fd
                ? new CanFdFrame(id, payload, BRS: brs, ESI: false) { IsExtendedFrame = ext }
                : new CanClassicFrame(id, payload, isExtendedFrame: ext);

            using var periodic = bus.TransmitPeriodic(frame, new PeriodicTxOptions(TimeSpan.FromMilliseconds(period), repeat));
            Console.WriteLine($"Periodic TX started: ep={endpoint} id=0x{id:X} dlc={frame.Dlc} fd={fd} brs={brs} period={period}ms count={repeat}");

            bus.FrameReceived += (_, e) =>
            {
                Console.WriteLine(RenderRx(e));
            };

            periodic.Completed += (_, __) => Console.WriteLine("Periodic TX completed.");

            Console.WriteLine("Press Enter to stop...");
            Console.ReadLine();
            return 0;
        }

        private static string RenderRx(CanReceiveData e)
        {
            var f = e.CanFrame;
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("] RX id=0x").Append(f.ID.ToString("X"));
            sb.Append(" dlc=").Append(f.Dlc).Append(' ');
            foreach (var b in f.Data.Span) sb.Append(b.ToString("X2")).Append(' ');
            return sb.ToString();
        }

        #region Tools

        private static string? GetArg(string[] args, string name) => args.SkipWhile(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        private static bool HasFlag(string[] args, string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        private static int ParseInt(string? s, int def) => int.TryParse(s, out var v) ? v : def;
        private static int ParseHex(string? s, int def)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;
            s = s!.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : def;
        }
        private static ReadOnlyMemory<byte> ParseHexBytes(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return ReadOnlyMemory<byte>.Empty;
            hex = hex.Replace(" ", "");
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
            if (hex.Length % 2 != 0) hex = "0" + hex;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(hex.Substring(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        #endregion

    }
}
