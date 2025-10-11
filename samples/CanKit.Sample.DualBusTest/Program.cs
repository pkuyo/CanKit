using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Sample.DualBusTest
{
    internal static class Program
    {
        // (try highest bitrate)
        private const int DefaultClassicBitrate = 1_000_000;      // 1M for CAN 2.0
        private const int DefaultFdArbBitrate   = 1_000_000;      // 1M arbitration
        private const int DefaultFdDataBitrate  = 8_000_000;      // 8M data

        private static ICanFrame[] seqFrames;

        private static async Task<int> Main(string[] args)
        {
            // Usage (examples at bottom):
            //  DualBusTest --a virtual://alpha/0 --b virtual://alpha/1 --count 1000 --fd --brs --ext --len 64 --mask 0x100 0x7FF
            //  DualBusTest --a kvaser://0 --b kvaser://1 --count 5000 --classic --bitrate 1000000
#pragma warning disable IDE0055
            var epA = GetArg(args, "--a") ?? "virtual://alpha/0"; // tester
            var epB = GetArg(args, "--b") ?? "virtual://alpha/1"; // DUT

            bool useFd     = HasFlag(args, "--fd");
            bool brs       = HasFlag(args, "--brs");
            bool extended  = HasFlag(args, "--ext");
            bool softFilter= HasFlag(args, "--softfilter");
            bool verbose   = !HasFlag(args, "--quiet");

            int bitrate    = ParseInt(GetArg(args, "--bitrate"), DefaultClassicBitrate);
            int abit       = ParseInt(GetArg(args, "--abit"),    DefaultFdArbBitrate);
            int dbit       = ParseInt(GetArg(args, "--dbit"),    DefaultFdDataBitrate);
            int count      = ParseInt(GetArg(args, "--count"),    2000);
            int frameLen   = ParseInt(GetArg(args, "--len"),      useFd ? 64 : 8);
            int batchSize  = ClampInt(ParseInt(GetArg(args, "--batch"), 64), 1, 4096);
            int gapUs      = ParseInt(GetArg(args, "--gapus"),    10); // inter-frame gap for TX slow down
            int timeOut    = ParseInt(GetArg(args, "--timeout"),  1000);
            int durationS  = ParseInt(GetArg(args, "--duration"), 0);
            int reportMs   = ParseInt(GetArg(args, "--report"),   1000);
            int asyncBuf   = ParseInt(GetArg(args, "--asyncbuf"), 8192);
            int ovf   = ParseInt(GetArg(args, "--ovf"), 10000);
            bool enableRes = (ParseInt(GetArg(args, "--res"),     0) == 1);

            // Filtering config (optional)
            bool hasMask   = TryGetTwoInts(args, "--mask", out int accCode, out int accMask);
            bool hasRange  = TryGetTwoInts(args, "--range", out int rangeMin, out int rangeMax);

            // Base ID (low 8 bits reserved for seq 0..255)
            var defaultBaseIdStd = 0x100;           // keep low 8 bits zero
            var defaultBaseIdExt = 0x18DAF100;      // keep low 8 bits zero
            uint baseId   = (uint)ParseInt(GetArg(args, "--baseid"), extended ? defaultBaseIdExt : defaultBaseIdStd);
            baseId &= extended ? 0x1FFFFF00u : 0x00000700u; // clear low 8 bits according to frame type
#pragma warning restore IDE0055

            // Which tests to run
            var mode = (GetArg(args, "--mode") ?? "all").ToLowerInvariant();

            if (verbose)
            {
                Console.WriteLine($"DualBusTest: A={epA} (tester), B={epB} (DUT), mode={mode}");
                Console.WriteLine(useFd
                    ? $"  FD: arb={abit} data={dbit} brs={(brs ? 1 : 0)} len={frameLen} ext={(extended ? 1 : 0)}"
                    : $"  Classic: bitrate={bitrate} len={frameLen} ext={(extended ? 1 : 0)}");
                if (hasMask)  Console.WriteLine($"  Filter: mask code=0x{accCode:X} mask=0x{accMask:X}");
                if (hasRange) Console.WriteLine($"  Filter: range min=0x{rangeMin:X} max=0x{rangeMax:X}");
            }

            using var busA = OpenBus(epA, configure: cfg =>
            {
                if (useFd) cfg.Fd(abit, dbit).SetProtocolMode(CanProtocolMode.CanFd).InternalRes(enableRes);
                else cfg.Baud(bitrate).SetProtocolMode(CanProtocolMode.Can20).InternalRes(enableRes);
                cfg.EnableErrorInfo().SetAsyncBufferCapacity(asyncBuf).SetReceiveLoopStopDelayMs(200).InternalRes(true);
                if (softFilter) cfg.SoftwareFeaturesFallBack(CanFeature.Filters);
            });
            using var busB = OpenBus(epB, configure: cfg =>
            {
                if (useFd) cfg.Fd(abit, dbit).SetProtocolMode(CanProtocolMode.CanFd).InternalRes(enableRes);
                else cfg.Baud(bitrate).SetProtocolMode(CanProtocolMode.Can20).InternalRes(enableRes);
                cfg.EnableErrorInfo().SetAsyncBufferCapacity(asyncBuf).SetReceiveLoopStopDelayMs(200).InternalRes(true);
                if (hasMask) cfg.AccMask(accCode, accMask, extended ? CanFilterIDType.Extend : CanFilterIDType.Standard);
                if (hasRange) cfg.RangeFilter(rangeMin, rangeMax, extended ? CanFilterIDType.Extend : CanFilterIDType.Standard);
                if (softFilter) cfg.SoftwareFeaturesFallBack(CanFeature.Filters);
            });

            seqFrames = CreateFrameRing(baseId, extended, useFd, brs, frameLen);

            // Decide which tests to run
            var all = string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase);
            var toRun = new List<Func<Task>>();

            if (all || mode.Equals("tx-sync", StringComparison.OrdinalIgnoreCase))
                toRun.Add(() => Test_DUT_Tx(busB, busA, count, batchSize, gapUs, timeOut, asyncTx: false, verbose, ovf));
            if (all || mode.Equals("tx-async", StringComparison.OrdinalIgnoreCase))
                toRun.Add(() => Test_DUT_Tx(busB, busA, count, batchSize, gapUs, timeOut, asyncTx: true, verbose, ovf));

            if (all || mode.Equals("rx-sync", StringComparison.OrdinalIgnoreCase))
                toRun.Add(() => Test_DUT_Rx(busA, busB,  count, batchSize, gapUs, timeOut, asyncRx: false, verbose, ovf));
            if (all || mode.Equals("rx-async", StringComparison.OrdinalIgnoreCase))
                toRun.Add(() => Test_DUT_Rx(busA, busB, count, batchSize, gapUs, timeOut, asyncRx: true, verbose, ovf));
            if (mode.Equals("event", StringComparison.OrdinalIgnoreCase))
                toRun.Add(() => Test_DUT_Rx_Event(busA, busB, count, batchSize, gapUs, timeOut, verbose, ovf));

            if (durationS > 0)
            {
                await LongRun(busA, busB, TimeSpan.FromSeconds(durationS), reportMs, gapUs);
                return 0;
            }

            foreach (var r in toRun)
                await r();

            if (verbose) Console.WriteLine("All selected tests completed.");
            return 0;
        }

        private static ICanBus OpenBus(string endpoint, Action<IBusInitOptionsConfigurator>? configure)
        {
            return CanBus.Open(endpoint, configure);
        }

        // Test: DUT(B) sending many frames; tester(A) receives and verifies
        private static async Task Test_DUT_Tx(ICanBus dutSender, ICanBus testerReceiver,
           int count, int batchSize, int gapUs, int rxTimeout, bool asyncTx, bool verbose, int ovf)
        {
            var testName = asyncTx ? "DUT TX async" : "DUT TX sync";
            if (verbose) Console.WriteLine($"== {testName} :: Send {count} frames, recv verify on tester ==");

            var verifier = new SequenceVerifier();
            using var onErr = SubscribeError(testerReceiver, verifier);

            var sw = Stopwatch.StartNew();
            var sendTask = Task.Run(async () => await SendBurst(dutSender, count, batchSize, gapUs, asyncTx, ovf));

            // Read until we think we've got all or timeout
            await ReceiveUntil(testerReceiver, verifier, expected: count, rxTimeout);

            await sendTask.ConfigureAwait(false);
            sw.Stop();

            PrintSummary(testName, count, verifier, sw.Elapsed, testerReceiver);
        }

        // Test: DUT(B) receiving many frames; tester(A) sends; verify on DUT(B)
        private static async Task Test_DUT_Rx(ICanBus testerSender, ICanBus dutReceiver,
          int count, int batchSize, int gapUs, int rxTimeout, bool asyncRx, bool verbose, int ovf)
        {
            var testName = asyncRx ? "DUT RX async" : "DUT RX sync";
            if (verbose) Console.WriteLine($"== {testName} :: Send {count} frames from tester, verify on DUT ==");

            var verifier = new SequenceVerifier();
            using var onErr = SubscribeError(dutReceiver, verifier);

            var sw = Stopwatch.StartNew();
            var sendTask = Task.Run(async () => await SendBurst(testerSender, count, batchSize, gapUs, asyncTx: true, ovf));

            if (asyncRx)
            {
                // Using ReceiveAsync in batches\
                while (verifier.Received < count && sw.ElapsedMilliseconds <= rxTimeout)
                {
                    var list = await dutReceiver.ReceiveAsync(Math.Min(256, count - verifier.Received), 500).ConfigureAwait(false);
                    foreach (var d in list) verifier.Feed(d.CanFrame);
                }
            }
            else
            {
                // Using synchronous Receive
                while (verifier.Received < count && sw.ElapsedMilliseconds <= rxTimeout)
                {
                    foreach (var d in dutReceiver.Receive(Math.Min(256, count - verifier.Received), 500))
                        verifier.Feed(d.CanFrame);
                }
            }

            await sendTask.ConfigureAwait(false);
            sw.Stop();

            PrintSummary(testName, count, verifier, sw.Elapsed, dutReceiver);
        }

        // Test: DUT(B) receiving via event handler only; tester(A) sends
        private static async Task Test_DUT_Rx_Event(ICanBus testerSender, ICanBus dutReceiver,
          int count, int batchSize, int gapUs, int rxTimeout, bool verbose, int ovf)
        {
            const string testName = "DUT RX event";
            if (verbose) Console.WriteLine($"== {testName} :: Send {count} frames from tester, verify on DUT(event) ==");

            var verifier = new SequenceVerifier();
            using var onErr = SubscribeError(dutReceiver, verifier);

            var sw = Stopwatch.StartNew();
            var sendTask = Task.Run(async () => await SendBurst(testerSender, count, batchSize, gapUs, asyncTx: true, ovf));
            await ReceiveUntil(dutReceiver, verifier,count, rxTimeout);
            await sendTask.ConfigureAwait(false);
            sw.Stop();

            PrintSummary(testName, count, verifier, sw.Elapsed, dutReceiver);
        }

        // Long-run test: continuously transmit + verify in both directions, with periodic stats
        private static async Task LongRun(ICanBus a, ICanBus b, TimeSpan duration, int reportMs, int gapUs)
        {
            Console.WriteLine($"== LongRun {duration.TotalSeconds}s, report={reportMs}ms ==");

            var vA = new SequenceVerifier();
            var vB = new SequenceVerifier();
            using var sA = SubscribeFrames(a, vA);
            using var sB = SubscribeFrames(b, vB);
            using var eA = SubscribeError(a, vA);
            using var eB = SubscribeError(b, vB);

            using var cts = new CancellationTokenSource(duration);

            var tA = Task.Run(async () =>
            {
                int seqSent = 0;
                while (!cts.IsCancellationRequested)
                {
                    var fr = GetFrame((byte)(seqSent & 0xFF));
                    await a.TransmitAsync(new[] { fr }).ConfigureAwait(false);
                    seqSent = (seqSent + 1) & 0xFF;
                    if (gapUs > 0) await Task.Delay(TimeSpan.FromMilliseconds(gapUs / 1000.0));

                }
            }, cts.Token);

            var tB = Task.Run(async () =>
            {
                int seqSent = 0;
                while (!cts.IsCancellationRequested)
                {
                    var fr = GetFrame((byte)(seqSent & 0xFF));
                    await b.TransmitAsync(new[] { fr }).ConfigureAwait(false);
                    seqSent = (seqSent + 1) & 0xFF;
                    if (gapUs > 0) await Task.Delay(TimeSpan.FromMilliseconds(gapUs / 1000.0));
                }
            }, cts.Token);

            var lastA = vA.GetSnapshot();
            var lastB = vB.GetSnapshot();
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(Math.Max(100, reportMs), cts.Token).ConfigureAwait(false);
                var curA = vA.GetSnapshot();
                var curB = vB.GetSnapshot();
                Console.WriteLine($"A: +{curA.Received - lastA.Received} rx, loss={curA.Lost}, dup={curA.Duplicates}, ooo={curA.OutOfOrder}, bad={curA.BadData}, errs={curA.ErrorFrames}");
                Console.WriteLine($"B: +{curB.Received - lastB.Received} rx, loss={curB.Lost}, dup={curB.Duplicates}, ooo={curB.OutOfOrder}, bad={curB.BadData}, errs={curB.ErrorFrames}");
                lastA = curA; lastB = curB;
            }

            cts.Cancel();
            await Task.WhenAll(Task.WhenAny(tA, Task.CompletedTask), Task.WhenAny(tB, Task.CompletedTask));
        }

        private static IDisposable SubscribeError(ICanBus bus, SequenceVerifier verifier)
        {
            EventHandler<ICanErrorInfo> onErr = (_, __) => verifier.RecordError();
            bus.ErrorFrameReceived += onErr;
            return new ActionOnDispose(() => bus.ErrorFrameReceived -= onErr);
        }

        private static IDisposable SubscribeFrames(ICanBus bus, SequenceVerifier verifier)
        {
            EventHandler<CanReceiveData> onRx = (_, e) => verifier.Feed(e.CanFrame);
            bus.FrameReceived += onRx;
            return new ActionOnDispose(() => bus.FrameReceived -= onRx);
        }

        private static async Task ReceiveUntil(ICanBus bus, SequenceVerifier verifier, int expected, int timeOut)
        {
            var sw = Stopwatch.StartNew();
            while (verifier.Received < expected && sw.ElapsedMilliseconds < timeOut)
            {
                var batch = await bus.ReceiveAsync(Math.Min(256, expected - verifier.Received),
                    (timeOut - (int)sw.ElapsedMilliseconds)).ConfigureAwait(false);
                foreach (var d in batch) verifier.Feed(d.CanFrame);
            }
        }

        private static async Task SendBurst(ICanBus tx, int count, int batchSize, int gapUs, bool asyncTx, int ovf)
        {
            int seq = 0;
            var send = 0;
            var queue = new Queue<ICanFrame>(batchSize);
            for (int i = 0; i < count; i++)
            {
                var fr = GetFrame((byte)(seq & 0xFF));
                queue.Enqueue(fr);
                seq = (seq + 1) & 0xFF;

                if (queue.Count >= batchSize || i == count - 1)
                {
                    if (asyncTx)
                        send = await tx.TransmitAsync(queue).ConfigureAwait(false);
                    else
                        send = tx.Transmit(queue);
                    bool overflow = send < 0;
                    if (overflow) send = -send;
                    for (int j = 0; j < send; j++)
                        queue.Dequeue();

                    if (overflow)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(ovf / 1000.0));
                    }
                    if (gapUs > 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(gapUs / 1000.0));
                    }
                }
            }
        }

        private static ICanFrame GetFrame(byte seq) => seqFrames[seq];

        private static ICanFrame[] CreateFrameRing(uint baseId, bool extended, bool useFd, bool brs, int len)
        {
            var ring = new ICanFrame[256];
            int L = Math.Max(len, 1);

            for (int seq = 0; seq < 256; seq++)
            {
                var payload = new byte[L];
                payload[0] = (byte)seq;
                for (int i = 1; i < L; i++) payload[i] = (byte)(i ^ seq);
                ReadOnlyMemory<byte> mem = payload;

                uint id = baseId | (uint)seq;
                ring[seq] = useFd
                    ? new CanFdFrame((int)id, mem, BRS: brs, ESI: false) { IsExtendedFrame = extended }
                    : new CanClassicFrame((int)id, mem, isExtendedFrame: extended);
            }
            return ring;
        }


        private static void PrintSummary(string name, int expected, SequenceVerifier v, TimeSpan elapsed, ICanBus bus)
        {
            var usage = 0f;
            try { usage = bus.BusUsage(); } catch { }
            Console.WriteLine($"[{name}] time={elapsed.TotalMilliseconds:F0}ms, expected={expected}, rx={v.Received}, loss={v.Lost}, dup={v.Duplicates}, ooo={v.OutOfOrder}, bad={v.BadData}, errs={v.ErrorFrames}, busUsage={usage:F1}%");
        }

        private sealed class SequenceVerifier
        {
            private readonly object _lock = new();

            private int _expected = 0;
            public int Received { get; private set; }
            public int Lost { get; private set; }
            public int Duplicates { get; private set; }
            public int OutOfOrder { get; private set; }
            public int BadData { get; private set; }
            public int ErrorFrames { get; private set; }

            public void Feed(ICanFrame fr)
            {
                lock (_lock)
                {
                    var seqFromId = (fr.ID & 0xFF);
                    var seqFromPayload = fr.Data.Length > 0 ? fr.Data.Span[0] : -1;
                    if (seqFromPayload != seqFromId) BadData++;

                    var delta = (seqFromId - _expected) & 0xFF;
                    if (delta == 0) _expected = (_expected + 1) & 0xFF;
                    else
                    {
                        if (delta == 0xFF) Duplicates++;
                        else if (delta < 0x80) Lost += delta;
                        else OutOfOrder++;
                        _expected = (seqFromId + 1) & 0xFF;
                    }

                    Received++;
                }
            }

            public Snapshot GetSnapshot()
            {
                lock (_lock) return new Snapshot(Received, Lost, Duplicates, OutOfOrder, BadData, ErrorFrames);
            }

            public void RecordError()
            {
                lock (_lock) ErrorFrames++;
            }

            public readonly struct Snapshot
            {
                public readonly int Received;
                public readonly int Lost;
                public readonly int Duplicates;
                public readonly int OutOfOrder;
                public readonly int BadData;
                public readonly int ErrorFrames;
                public Snapshot(int received, int lost, int duplicates, int outOfOrder, int badData, int errorFrames)
                {
                    Received = received;
                    Lost = lost;
                    Duplicates = duplicates;
                    OutOfOrder = outOfOrder;
                    BadData = badData;
                    ErrorFrames = errorFrames;
                }
            }
        }

        private sealed class ActionOnDispose : IDisposable
        {
            private readonly Action _a;
            public ActionOnDispose(Action a) => _a = a;
            public void Dispose() { try { _a(); } catch { } }
        }

        private static string? GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private static bool TryGetTwoInts(string[] args, string name, out int a, out int b)
        {
            a = b = 0;
            for (int i = 0; i < args.Length - 2; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    a = ParseInt(args[i + 1], 0);
                    b = ParseInt(args[i + 2], 0);
                    return true;
                }
            }
            return false;
        }

        private static bool HasFlag(string[] args, string name)
        {
            foreach (var a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static int ParseInt(string? s, int def)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hx))
                return hx;
            return int.TryParse(s, out var v) ? v : def;
        }

        private static int ClampInt(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
