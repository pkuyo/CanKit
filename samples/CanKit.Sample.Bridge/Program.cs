using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core;
using CanKit.Core.Definitions;

namespace CanKit.Sample.Bridge
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // Usage: Bridge --src <ep> --dst <ep> [--bidir] [--seconds 0] [--res 1]
            var src = GetArg(args, "--src") ?? "virtual://alpha/0";
            var dst = GetArg(args, "--dst") ?? "virtual://alpha/1";
            bool bidir = HasFlag(args, "--bidir");
            bool enableRes = (ParseUInt(GetArg(args, "--res"), 1) == 1U);
            int seconds = (int)ParseUInt(GetArg(args, "--seconds"), 0);

            using var srcBus = CanBus.Open(src, cfg => cfg.SetProtocolMode(CanProtocolMode.CanFd).InternalRes(enableRes));
            using var dstBus = CanBus.Open(dst, cfg => cfg.SetProtocolMode(CanProtocolMode.CanFd).InternalRes(enableRes));

            using var cts = seconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            Console.WriteLine($"Bridging {src} => {dst} {(bidir ? "+ reverse" : "")}. Ctrl+C to stop.");

            var tasks = new List<Task>
            {
                Pump(srcBus, dstBus, cts.Token, $"{Now()} src->dst"),
            };
            if (bidir) tasks.Add(Pump(dstBus, srcBus, cts.Token, $"{Now()} dst->src"));

            await Task.WhenAll(tasks);
            return 0;
        }

        private static async Task Pump(CanKit.Core.Abstractions.ICanBus from, CanKit.Core.Abstractions.ICanBus to, CancellationToken ct, string tag)
        {
#if NET8_0_OR_GREATER
            await foreach (var e in from.GetFramesAsync(ct))
            {
                await to.TransmitAsync(new[] { new CanTransmitData(e.CanFrame) }, 0, ct);
            }
#else
            while (!ct.IsCancellationRequested)
            {
                var list = await from.ReceiveAsync(32, 50, ct);
                foreach (var e in list)
                    await to.TransmitAsync(new[] { new CanTransmitData(e.CanFrame) }, 0, ct);
            }
#endif
        }

        private static string Now() => DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        private static string? GetArg(string[] args, string name) => args.SkipWhile(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        private static bool HasFlag(string[] args, string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        private static uint ParseUInt(string? s, uint def) => uint.TryParse(s, out var v) ? v : def;
    }
}

