using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core;
using CanKit.Core.Definitions;

namespace CanKit.Sample.Sniffer
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // Usage: Sniffer --endpoint <ep> [--listen-only] [--bitrate 500000] [--dbitrate 2000000] [--fd] [--range 0x100-0x200] [--mask 0x123:0x7FF] [--seconds 0] [--res 1]
            var endpoint = GetArg(args, "--endpoint") ?? "virtual://alpha/0";
            bool listenOnly = HasFlag(args, "--listen-only");
            bool useFd = HasFlag(args, "--fd");
            var range = GetArg(args, "--range");
            var mask = GetArg(args, "--mask");
            uint bitrate = ParseUInt(GetArg(args, "--bitrate"), 500_000);
            uint dbitrate = ParseUInt(GetArg(args, "--dbitrate"), 2_000_000);
            int seconds = (int)ParseUInt(GetArg(args, "--seconds"), 0);
            bool enableRes = (ParseUInt(GetArg(args, "--res"), 1) == 1U);

            using var bus = CanBus.Open(endpoint, cfg =>
            {
                if (useFd) cfg.SetProtocolMode(CanProtocolMode.CanFd).Fd(bitrate, dbitrate);
                else cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(bitrate);
                if (listenOnly) cfg.SetWorkMode(ChannelWorkMode.ListenOnly);
                cfg.EnableErrorInfo()
                    .InternalRes(enableRes);
                if (!string.IsNullOrWhiteSpace(range))
                {
                    var (min, max, ext) = ParseRange(range!);
                    cfg.RangeFilter(min, max, ext ? CanFilterIDType.Extend : CanFilterIDType.Standard);
                }
                if (!string.IsNullOrWhiteSpace(mask))
                {
                    var (acc, m, ext) = ParseMask(mask!);
                    cfg.AccMask(acc, m, ext ? CanFilterIDType.Extend : CanFilterIDType.Standard);
                }
            });

            using var cts = seconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            Console.WriteLine($"Sniffing on {endpoint} (Press Ctrl+C to stop)");

#if NET8_0_OR_GREATER
            await foreach (var e in bus.GetFramesAsync(cts.Token))
            {
                LogFrame(e);
            }
#else
            bus.FrameReceived += (_, e) => LogFrame(e);
            cts.Token.WaitHandle.WaitOne();
            await Task.Delay(1); //disable warning CS1998
#endif
            return 0;
        }

        private static void LogFrame(CanReceiveData e)
        {
            var f = e.CanFrame;
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("] ");
            sb.Append("RX ");
            sb.Append(f.FrameKind == CanFrameType.CanFd ? "FD" : "CL");
            sb.Append(' ');
            sb.Append(((f.RawID & 0x8000_0000) != 0) ? "ext" : "std");
            sb.Append(" id=0x").Append(f.ID.ToString("X"));
            sb.Append(" dlc=").Append(f.Dlc);
            if (f is CanFdFrame fd && fd.BitRateSwitch) sb.Append(" brs");
            sb.Append(" data=");
            var span = f.Data.Span;
            for (int i = 0; i < span.Length; i++) sb.Append(span[i].ToString("X2")).Append(' ');
            Console.WriteLine(sb.ToString());
        }

        private static (uint min, uint max, bool ext) ParseRange(string s)
        {
            // Example: 0x100-0x200 or 100-200 or x:ext
#if NET8_0_OR_GREATER
            var p = s.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
#else
            var p = s.Split(['-'], StringSplitOptions.RemoveEmptyEntries);
#endif
            if (p.Length >= 2)
            {
                var min = Convert.ToUInt32(p[0], 16);
                var max = Convert.ToUInt32(p[1].Split([':'])[0], 16);
                bool ext = s.EndsWith(":ext", StringComparison.OrdinalIgnoreCase);
                return (min, max, ext);
            }
            return (0, 0x7FF, false);
        }

        private static (uint acc, uint mask, bool ext) ParseMask(string s)
        {
            // Example: 0x123:0x7FF or 123:7FF or :ext appended
#if NET8_0_OR_GREATER
            var p = s.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
#else
            var p = s.Split([':'], StringSplitOptions.RemoveEmptyEntries);
#endif
            uint acc = p.Length > 0 ? Convert.ToUInt32(p[0], 16) : 0;
            uint mask = p.Length > 1 ? Convert.ToUInt32(p[1], 16) : 0x7FF;
            bool ext = s.EndsWith(":ext", StringComparison.OrdinalIgnoreCase);
            return (acc, mask, ext);
        }

        private static string? GetArg(string[] args, string name) => args.SkipWhile(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        private static bool HasFlag(string[] args, string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        private static uint ParseUInt(string? s, uint def) => uint.TryParse(s, out var v) ? v : def;
    }
}

