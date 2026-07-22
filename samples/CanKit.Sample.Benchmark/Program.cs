using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Core.Definitions;
using CanKit.Pro.IsoTp;

namespace CanKit.Sample.Benchmark
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // Usage: Benchmark [--src <ep>] [--dst <ep>] [--frames 10000] [--len 8] [--fd] [--brs]  [--bitrate 500000] [--dbitrate 2000000] [--res 1] [--alloc]

            #region ParseArgs

            var src = GetArg(args, "--src") ?? "virtual://alpha/0";
            var dst = GetArg(args, "--dst") ?? "virtual://alpha/1";
            int frames = ParseInt(GetArg(args, "--frames"), 50_000);
            int bitrate = ParseInt(GetArg(args, "--bitrate"), 500_000);
            int dbitrate = ParseInt(GetArg(args, "--dbitrate"), 2_000_000);
            int len = ParseInt(GetArg(args, "--len"), 8);
            bool fd = HasFlag(args, "--fd");
            bool brs = HasFlag(args, "--brs");
            bool enableRes = (ParseInt(GetArg(args, "--res"), 1) == 1);
            int gapMs = int.TryParse(GetArg(args, "--gapms"), out var s) ? s : -1;

            #endregion

#if NET8_0_OR_GREATER
            if (HasFlag(args, "--alloc"))
            {
                return await RunAllocationBenchmarkAsync();
            }
#else
            if (HasFlag(args, "--alloc"))
            {
                Console.WriteLine("--alloc requires the net8.0 build (GC allocation measurement APIs).");
                return 2;
            }
#endif

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
            var sw = Stopwatch.StartNew();
            int received = 0;
            long lastReceived = sw.ElapsedTicks;
            var cts = new CancellationTokenSource();

#if NET8_0_OR_GREATER
            var rxTask = Task.Run(async () =>
            {
                await foreach (var e in rx.GetFramesAsync(cts.Token))
                {
                    if (Interlocked.Increment(ref received) >= frames) { break; }
                    Interlocked.Exchange(ref lastReceived, sw.ElapsedTicks);
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
                    Interlocked.Exchange(ref lastReceived, sw.ElapsedTicks);
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
            var frame = fd ? CanFrame.Fd(id, payload, BRS: brs, ESI: false) : CanFrame.Classic(id, payload);


            const int batch = 64;
            int sent = 0;
            while (sent < frames)
            {
                int take = Math.Min(batch, frames - sent);
                if (gapMs >= 0)
                {
                    if (sent > 0) Thread.Sleep(gapMs);
                }
                else
                {
                    Thread.Sleep(1);
                }
                var list = new CanFrame[take];
                for (int i = 0; i < take; i++) list[i] = frame;
                take = await tx.TransmitAsync(list, -1);
                sent += take;
            }
            cts.CancelAfter(TimeSpan.FromMilliseconds(1000));
            var totalRx = await done.Task; // wait until received expected
            sw.Stop();
            var totalTime = TimeSpan.FromTicks(lastReceived);
            var secs = Math.Max(1e-6, totalTime.TotalSeconds);
            var rate = totalRx / secs;
            Console.WriteLine($"Frames: {frames}, Received:{received} Bytes/Frame: {payload.Length}, FD={fd}, BRS={brs}");
            Console.WriteLine($"Elapsed: {totalTime.TotalMilliseconds:F1} ms, Throughput: {rate:F0} frames/s");
            return 0;
        }

        #region Tools

#if NET8_0_OR_GREATER
        // Allocation benchmark for the ISO-TP SF/CF hot paths (NFR-007). Deliberately uses
        // GC.GetTotalAllocatedBytes rather than BenchmarkDotNet's MemoryDiagnoser: the
        // per-thread diagnoser only sees the calling thread, but an actor-driven pipeline
        // allocates on actor / event-pump threads — exactly the part we want to measure.
        // The process-wide counter covers all of it, and no new package dependency is needed
        // in the samples tree.
        private static async Task<int> RunAllocationBenchmarkAsync()
        {
            static async Task<(long bytes, int ops)> MeasureAsync(int ops, Func<Task> op)
            {
                // Warmup: settle JIT + reusable buffers before measuring.
                for (var i = 0; i < Math.Min(100, ops / 10 + 1); i++) await op();
                var before = GC.GetTotalAllocatedBytes(precise: false);
                for (var i = 0; i < ops; i++) await op();
                return (GC.GetTotalAllocatedBytes(precise: false) - before, ops);
            }

            var session = $"alloc-{Guid.NewGuid():N}";
            using var busA = CanBus.Open($"virtual://{session}/0", cfg => cfg.Baud(500_000));
            using var busB = CanBus.Open($"virtual://{session}/1", cfg => cfg.Baud(500_000));

            var options = new IsoTpChannelOptions { UsePadding = true };
            using var sender = IsoTp.Open(busA, IsoTpEndpoint.Normal(0x7E0, 0x7E8), options);
            using var receiver = IsoTp.Open(busB, IsoTpEndpoint.Normal(0x7E8, 0x7E0), options);
            // The receiver's reassembly inbox is intentionally not drained: drop-oldest keeps
            // it non-blocking, and the TX path is what we measure.

            var sfPdu = new byte[] { 0x22, 0xF1, 0x89 };
            var mfPdu = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();

            var l1 = await MeasureAsync(5000,
                () => { busA.Transmit(CanFrame.Classic(0x100, sfPdu)); return Task.CompletedTask; });
            var sf = await MeasureAsync(2000, () => sender.SendAsync(sfPdu));
            var mf = await MeasureAsync(200, () => sender.SendAsync(mfPdu));

            Console.WriteLine("Allocation benchmark (all threads, Virtual loopback):");
            Print("L1 Transmit 1F", l1);
            Print("ISO-TP SF", sf);
            Print("ISO-TP MF (~29 CF)", mf);
            return 0;

            static void Print(string name, (long bytes, int ops) r)
                => Console.WriteLine($"  {name,-18}: {r.bytes / (double)r.ops,10:F1} B/op" +
                                     $"   (total {r.bytes:N0} B over {r.ops} ops)");
        }
#endif

        private static string? GetArg(string[] args, string name) => args.SkipWhile(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        private static bool HasFlag(string[] args, string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        private static int ParseInt(string? s, int def) => int.TryParse(s, out var v) ? v : def;

        #endregion

    }
}
