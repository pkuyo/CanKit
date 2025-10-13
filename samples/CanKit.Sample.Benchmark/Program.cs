using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core;
using CanKit.Core.Definitions;

namespace CanKit.Sample.Benchmark
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // Usage: Benchmark [--src <ep>] [--dst <ep>] [--frames 10000] [--len 8] [--fd] [--brs]  [--bitrate 500000] [--dbitrate 2000000] [--res 1]
            var src = GetArg(args, "--src") ?? "virtual://alpha/0";
            var dst = GetArg(args, "--dst") ?? "virtual://alpha/1";
            int frames = ParseInt(GetArg(args, "--frames"), 50_000);
            int bitrate = ParseInt(GetArg(args, "--bitrate"), 500_000);
            int dbitrate = ParseInt(GetArg(args, "--dbitrate"), 2_000_000);
            int len = ParseInt(GetArg(args, "--len"), 8);
            bool fd = HasFlag(args, "--fd");
            bool brs = HasFlag(args, "--brs");
            bool enableRes = (ParseInt(GetArg(args, "--res"), 1) == 1);
            int sleepMsOverride = int.TryParse(GetArg(args, "--sleepms"), out var s) ? s : -1;
            double util = double.TryParse(GetArg(args, "--util"), NumberStyles.Float, CultureInfo.InvariantCulture, out var u) ? u : 0.70;
            double stuff = double.TryParse(GetArg(args, "--stuff"), NumberStyles.Float, CultureInfo.InvariantCulture, out var st) ? st : 1.20;

            using var rx = CanBus.Open(dst, cfg =>
            {
                if (fd)
                {
                    cfg.Fd(bitrate, dbitrate).SetProtocolMode(CanProtocolMode.CanFd);
                }
                else
                {
                    cfg.Baud(bitrate).SetProtocolMode(CanProtocolMode.Can20);
                }

                cfg.InternalRes(enableRes);
            });
            using var tx = CanBus.Open(src, cfg =>
            {
                if (fd)
                {
                    cfg.Fd(bitrate, dbitrate).SetProtocolMode(CanProtocolMode.CanFd);
                }
                else
                {
                    cfg.Baud(bitrate).SetProtocolMode(CanProtocolMode.Can20);
                }
                cfg.InternalRes(enableRes);
            });

            var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            int received = 0;
            var cts = new CancellationTokenSource();
#if NET8_0_OR_GREATER

            var rxTask = Task.Run(async () =>
            {
                await foreach (var e in rx.GetFramesAsync(cts.Token))
                {
                    if (Interlocked.Increment(ref received) >= frames) { break; }
                }
            }, cts.Token).ContinueWith((t) =>
            {
                _ = t.Exception;
                done.TrySetResult(received);
            });
#else
            var rxTask = Task.Run(async () =>
            {
                while (Interlocked.CompareExchange(ref received, 0, 0) < frames)
                {
                    var list = await rx.ReceiveAsync(256, 10);
                    Interlocked.Add(ref received, list.Count);
                }
                done.TrySetResult(received);
            }, cts.Token).ContinueWith((t) =>
            {
                _ = t.Exception;
                done.TrySetResult(received);
            });
#endif
            var id = 0x100;
            var payload = new byte[Math.Max(0, Math.Min(len, fd ? 64 : 8))];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i & 0xFF);
            var frame = fd ? (ICanFrame)new CanFdFrame(id, payload, BRS: brs, ESI: false) : new CanClassicFrame(id, payload);

            double perFrameUs = FrameTimeUsEstimate(fd, brs, payload.Length, bitrate, dbitrate, stuff);
            double perFramePeriodUs = perFrameUs / util;

            var sw = Stopwatch.StartNew();
            const int batch = 64;
            int sent = 0;
            while (sent < frames)
            {
                int take = Math.Min(batch, frames - sent);
                if (sleepMsOverride >= 0)
                {
                    if (sent > 0) Thread.Sleep(sleepMsOverride);
                }
                else
                {
                    double batchUs = perFramePeriodUs * take;
                    int sleepMs = (int)Math.Round(batchUs / 1000.0);
                    if (sleepMs > 0 && sent > 0) Thread.Sleep(sleepMs);
                }
                var list = new ICanFrame[take];
                for (int i = 0; i < take; i++) list[i] = frame;
                await tx.TransmitAsync(list, -1);
                sent += take;
            }
            cts.CancelAfter(TimeSpan.FromMilliseconds(1000));
            var totalRx = await done.Task; // wait until received expected
            sw.Stop();

            double secs = Math.Max(1e-6, sw.Elapsed.TotalSeconds);
            double rate = totalRx / secs;
            Console.WriteLine($"Frames: {frames}, Received:{received} Bytes/Frame: {payload.Length}, FD={fd}, BRS={brs}");
            Console.WriteLine($"Elapsed: {sw.Elapsed.TotalMilliseconds:F1} ms, Throughput: {rate:F0} frames/s");
            return 0;
        }


        private static double FrameTimeUsEstimate(bool isFd, bool brsOn, int bytes, int arbBitrate, int dataBitrate, double stuffFactor)
        {
            bytes = Math.Max(0, Math.Min(bytes, isFd ? 64 : 8));
            if (!isFd)
            {
                double bits = (47.0 + 8.0 * bytes) * stuffFactor;
                return bits / arbBitrate * 1e6;
            }
            else
            {
                double arbBitsNoStuff = 36.0 + (bytes <= 16 ? 17.0 : 21.0);
                double dataBits = 8.0 * bytes;
                double tArbUs = (arbBitsNoStuff * stuffFactor) / arbBitrate * 1e6;
                double tDataUs = (dataBits        * stuffFactor) / (brsOn ? dataBitrate : arbBitrate) * 1e6;
                return tArbUs + tDataUs;
            }
        }
        private static string? GetArg(string[] args, string name) => args.SkipWhile(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        private static bool HasFlag(string[] args, string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        private static int ParseInt(string? s, int def) => int.TryParse(s, out var v) ? v : def;
    }
}
