using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Sample.DualBusTest;

internal static class Program
{
    // (try highest bitrate)
    private const int DefaultClassicBitrate = 1_000_000; // 1M for CAN 2.0
    private const int DefaultFdArbBitrate = 1_000_000; // 1M arbitration
    private const int DefaultFdDataBitrate = 8_000_000; // 8M data

    private static ICanFrame[]? _seqFrames;

    private static async Task<int> Main(string[] args)
    {
        // Usage (examples at bottom):
        //  DualBusTest --a virtual://alpha/0 --b virtual://alpha/1 --count 1000 --fd --brs --ext --len 64 --mask 0x100 0x7FF
        //  DualBusTest --a kvaser://0 --b kvaser://1 --count 5000 --classic --bitrate 1000000

        var epA = GetArg(args, "--a") ?? "virtual://alpha/0"; // tester
        var epB = GetArg(args, "--b") ?? "virtual://alpha/1"; // DUT

        var useFd = HasFlag(args, "--fd");
        var brs = HasFlag(args, "--brs");
        var extended = HasFlag(args, "--ext");
        var softFilter = HasFlag(args, "--softfilter");
        var verbose = !HasFlag(args, "--quiet");

        var bitrate = ParseInt(GetArg(args, "--bitrate"), DefaultClassicBitrate);
        var abit = ParseInt(GetArg(args, "--abit"), DefaultFdArbBitrate);
        var dbit = ParseInt(GetArg(args, "--dbit"), DefaultFdDataBitrate);
        var count = ParseInt(GetArg(args, "--count"), 2000);
        var frameLen = ParseInt(GetArg(args, "--len"), useFd ? 64 : 8);
        var batchSize = ClampInt(ParseInt(GetArg(args, "--batch"), 64), 1, 4096);
        var gapMs = ParseInt(GetArg(args, "--gapms"), 1);
        var timeOut = ParseInt(GetArg(args, "--timeout"), 1000);
        var durationS = ParseInt(GetArg(args, "--duration"), 0);
        var reportMs = ParseInt(GetArg(args, "--report"), 1000);
        var asyncBuf = ParseInt(GetArg(args, "--asyncbuf"), 8192);
        var enableRes = ParseInt(GetArg(args, "--res"), 0) == 1;

        // Filtering config (optional)
        var hasMask = TryGetTwoInts(args, "--mask", out var accCode, out var accMask);
        var hasRange = TryGetTwoInts(args, "--range", out var rangeMin, out var rangeMax);

        // Base ID (low 8 bits reserved for seq 0..255)
        var defaultBaseIdStd = 0x100; // keep low 8 bits zero
        var defaultBaseIdExt = 0x18DAF100; // keep low 8 bits zero
        var baseId = (uint)ParseInt(GetArg(args, "--baseid"), extended ? defaultBaseIdExt : defaultBaseIdStd);
        baseId &= extended ? 0x1FFFFF00u : 0x00000700u; // clear low 8 bits according to frame type


        // Which tests to run
        var mode = (GetArg(args, "--mode") ?? "all").ToLowerInvariant();

        if (verbose)
        {
            Console.WriteLine($"DualBusTest: A={epA} (tester), B={epB} (DUT), mode={mode}");
            Console.WriteLine(useFd
                ? $"  FD: arb={abit} data={dbit} brs={(brs ? 1 : 0)} len={frameLen} ext={(extended ? 1 : 0)}"
                : $"  Classic: bitrate={bitrate} len={frameLen} ext={(extended ? 1 : 0)}");
            if (hasMask)
            {
                Console.WriteLine($"  Filter: mask code=0x{accCode:X} mask=0x{accMask:X}");
            }

            if (hasRange)
            {
                Console.WriteLine($"  Filter: range min=0x{rangeMin:X} max=0x{rangeMax:X}");
            }
        }

        using var busA = OpenBus(epA, cfg =>
        {
            if (useFd)
            {
                cfg.Fd(abit, dbit).SetProtocolMode(CanProtocolMode.CanFd).InternalRes(enableRes);
            }
            else
            {
                cfg.Baud(bitrate).SetProtocolMode(CanProtocolMode.Can20).InternalRes(enableRes);
            }

            cfg.EnableErrorInfo().SetAsyncBufferCapacity(asyncBuf).SetReceiveLoopStopDelayMs(200).InternalRes(true);
            if (softFilter)
            {
                cfg.SoftwareFeaturesFallBack(CanFeature.Filters);
            }
        });
        using var busB = OpenBus(epB, cfg =>
        {
            if (useFd)
            {
                cfg.Fd(abit, dbit).SetProtocolMode(CanProtocolMode.CanFd).InternalRes(enableRes);
            }
            else
            {
                cfg.Baud(bitrate).SetProtocolMode(CanProtocolMode.Can20).InternalRes(enableRes);
            }

            cfg.EnableErrorInfo().SetAsyncBufferCapacity(asyncBuf).SetReceiveLoopStopDelayMs(200).InternalRes(true);
            if (hasMask)
            {
                cfg.AccMask(accCode, accMask, extended ? CanFilterIDType.Extend : CanFilterIDType.Standard);
            }

            if (hasRange)
            {
                cfg.RangeFilter(rangeMin, rangeMax, extended ? CanFilterIDType.Extend : CanFilterIDType.Standard);
            }

            if (softFilter)
            {
                cfg.SoftwareFeaturesFallBack(CanFeature.Filters);
            }
        });

        _seqFrames = CreateFrameRing(baseId, extended, useFd, brs, frameLen);

        // Decide which tests to run
        var all = string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase);
        var toRun = new List<Func<ICanBus, ICanBus, Task>>();

        if (all || mode.Equals("tx-sync", StringComparison.OrdinalIgnoreCase))
        {
            toRun.Add((a, b) => Test_DUT_Tx(a, b, count, batchSize, gapMs, timeOut, asyncTx: false, verbose));
        }

        if (all || mode.Equals("tx-async", StringComparison.OrdinalIgnoreCase))
        {
            toRun.Add((a, b) => Test_DUT_Tx(a, b, count, batchSize, gapMs, timeOut, asyncTx: true, verbose));
        }

        if (all || mode.Equals("rx-sync", StringComparison.OrdinalIgnoreCase))
        {
            toRun.Add((a, b) => Test_DUT_Rx(a, b, count, batchSize, gapMs, timeOut, false, verbose));
        }

        if (all || mode.Equals("rx-async", StringComparison.OrdinalIgnoreCase))
        {
            toRun.Add((a, b) => Test_DUT_Rx(a, b, count, batchSize, gapMs, timeOut, true, verbose));
        }

        if (mode.Equals("event", StringComparison.OrdinalIgnoreCase))
        {
            toRun.Add((a, b) => Test_DUT_Rx_Event(a, b, count, batchSize, gapMs, timeOut, verbose));
        }

        foreach (var r in toRun)
        {
            await r(busA, busB);
        }

        if (durationS > 0)
        {
            await LongRun(busA, busB, TimeSpan.FromSeconds(durationS), reportMs, gapMs);
            return 0;
        }

        if (verbose)
        {
            Console.WriteLine("All selected tests completed.");
        }

        return 0;
    }

    private static ICanBus OpenBus(string endpoint, Action<IBusInitOptionsConfigurator>? configure) =>
        CanBus.Open(endpoint, configure);

    // Test: DUT(B) sending many frames; tester(A) receives and verifies
    private static async Task Test_DUT_Tx(ICanBus testerReceiver, ICanBus dutSender,
        int count, int batchSize, int gapMs, int rxTimeout, bool asyncTx, bool verbose)
    {
        var testName = asyncTx ? "DUT TX async" : "DUT TX sync";
        if (verbose)
        {
            Console.WriteLine($"== {testName} :: Send {count} frames, recv verify on tester ==");
        }

        var verifier = new SequenceVerifier();
        using var onErr = SubscribeError(testerReceiver, verifier);

        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        var recTask = Task.Run(async () => await Receive(testerReceiver, verifier, count, token));

        await SendBurst(dutSender, count, batchSize, gapMs, asyncTx);
        await Task.Delay(rxTimeout);
        cts.Cancel();

        await recTask.ContinueWith(IgnoreCancelException);

        sw.Stop();
        PrintSummary(testName, count, verifier, sw.Elapsed, testerReceiver);
    }

    // Test: DUT(B) receiving many frames; tester(A) sends; verify on DUT(B)
    private static async Task Test_DUT_Rx(ICanBus testerSender, ICanBus dutReceiver,
        int count, int batchSize, int gapMs, int rxTimeout, bool asyncRx, bool verbose)
    {
        var testName = asyncRx ? "DUT RX async" : "DUT RX sync";
        if (verbose)
        {
            Console.WriteLine($"== {testName} :: Send {count} frames from tester, verify on DUT ==");
        }

        var verifier = new SequenceVerifier();
        using var onErr = SubscribeError(dutReceiver, verifier);

        var sw = Stopwatch.StartNew();

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        var recTask = Task.Run(async () =>
        {
            if (asyncRx)
            {
                // Using ReceiveAsync in batches\
                while (verifier.Received < count && !token.IsCancellationRequested)
                {
                    var list = await dutReceiver
                        .ReceiveAsync(Math.Min(256, count - verifier.Received), 200)
                        .ConfigureAwait(false);
                    foreach (var d in list)
                    {
                        verifier.Feed(d.CanFrame);
                    }
                }
            }
            else
            {
                // Using synchronous Receive
                while (verifier.Received < count && !token.IsCancellationRequested)
                {
                    foreach (var d in dutReceiver.Receive(Math.Min(256, count - verifier.Received), 200))
                    {
                        verifier.Feed(d.CanFrame);
                    }
                }
            }
        }, cts.Token);

        await SendBurst(testerSender, count, batchSize, gapMs, true);
        await Task.Delay(rxTimeout);
        cts.Cancel();

        await recTask.ContinueWith(IgnoreCancelException);

        sw.Stop();
        PrintSummary(testName, count, verifier, sw.Elapsed, dutReceiver);
    }

    // Test: DUT(B) receiving via event handler only; tester(A) sends
    private static async Task Test_DUT_Rx_Event(ICanBus testerSender, ICanBus dutReceiver,
        int count, int batchSize, int gapMs, int rxTimeout, bool verbose)
    {
        const string testName = "DUT RX event";
        if (verbose)
        {
            Console.WriteLine($"== {testName} :: Send {count} frames from tester, verify on DUT(event) ==");
        }

        var verifier = new SequenceVerifier();
        using var onErr = SubscribeError(dutReceiver, verifier);

        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        var recTask = Task.Run(async () => await Receive(dutReceiver, verifier, count, token));
        await SendBurst(testerSender, count, batchSize, gapMs, true);
        await Task.Delay(rxTimeout);
        cts.Cancel();

        await recTask.ContinueWith(IgnoreCancelException);

        sw.Stop();
        PrintSummary(testName, count, verifier, sw.Elapsed, dutReceiver);
    }

    // Long-run test: continuously transmit + verify in both directions, with periodic stats
    private static async Task LongRun(ICanBus a, ICanBus b, TimeSpan duration, int reportMs, int gapMs)
    {
        Console.WriteLine($"== LongRun {duration.TotalSeconds}s, report={reportMs}ms ==");

        var vA = new SequenceVerifier();
        var vB = new SequenceVerifier();
        using var sA = SubscribeFrames(a, vA);
        using var sB = SubscribeFrames(b, vB);
        using var eA = SubscribeError(a, vA);
        using var eB = SubscribeError(b, vB);

        using var cts = new CancellationTokenSource(duration);
        var token = cts.Token;

        var tA = Task.Run(async () =>
        {
            var seqSent = 0;
            while (!token.IsCancellationRequested)
            {
                var fr = GetFrame((byte)(seqSent & 0xFF));
                await a.TransmitAsync([fr]).ConfigureAwait(false);
                seqSent = (seqSent + 1) & 0xFF;
                if (gapMs > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(gapMs));
                }
            }
        }, cts.Token);

        var tB = Task.Run(async () =>
        {
            var seqSent = 0;
            while (!token.IsCancellationRequested)
            {
                var fr = GetFrame((byte)(seqSent & 0xFF));
                await b.TransmitAsync([fr]).ConfigureAwait(false);
                seqSent = (seqSent + 1) & 0xFF;
                if (gapMs > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(gapMs / 1000.0));
                }
            }
        }, cts.Token);

        var lastA = vA.GetSnapshot();
        var lastB = vB.GetSnapshot();
        while (!cts.IsCancellationRequested)
        {
            await Task.Delay(Math.Max(100, reportMs), cts.Token).ConfigureAwait(false);
            var curA = vA.GetSnapshot();
            var curB = vB.GetSnapshot();
            Console.WriteLine(
                $"A: +{curA.received - lastA.received} rx, loss={curA.lost}, dup={curA.duplicates}, ooo={curA.outOfOrder}, bad={curA.badData}, errs={curA.errorFrames}");
            Console.WriteLine(
                $"B: +{curB.received - lastB.received} rx, loss={curB.lost}, dup={curB.duplicates}, ooo={curB.outOfOrder}, bad={curB.badData}, errs={curB.errorFrames}");
            lastA = curA;
            lastB = curB;
        }

        cts.Cancel();
        await Task.WhenAll(Task.WhenAny(tA, Task.CompletedTask), Task.WhenAny(tB, Task.CompletedTask));
    }

    private static void IgnoreCancelException(Task t)
    {
        if (t.Exception != null)
        {
            foreach (var e in t.Exception.InnerExceptions)
            {
                if (e is not OperationCanceledException)
                {
                    throw t.Exception;
                }
            }
        }
    }

    private static IDisposable SubscribeError(ICanBus bus, SequenceVerifier verifier)
    {
        EventHandler<ICanErrorInfo> onErr = (_, info) => { verifier.RecordError(info); };
        bus.ErrorFrameReceived += onErr;
        return new ActionOnDispose(() => bus.ErrorFrameReceived -= onErr);
    }

    private static IDisposable SubscribeFrames(ICanBus bus, SequenceVerifier verifier)
    {
        EventHandler<CanReceiveData> onRx = (_, e) => verifier.Feed(e.CanFrame);
        bus.FrameReceived += onRx;
        return new ActionOnDispose(() => bus.FrameReceived -= onRx);
    }

    private static async Task Receive(ICanBus bus, SequenceVerifier verifier, int expected, CancellationToken ct)
    {
        while (verifier.Received < expected && !ct.IsCancellationRequested)
        {
            var batch =
                await bus.ReceiveAsync(Math.Min(256, expected - verifier.Received), 200, ct).ConfigureAwait(false);
            foreach (var d in batch)
            {
                verifier.Feed(d.CanFrame);
            }
        }
    }

    private static async Task SendBurst(ICanBus tx, int count, int batchSize, int gapMs, bool asyncTx)
    {
        var seq = 0;
        var queue = new Queue<ICanFrame>(batchSize);

        for (var i = 0; i < count; i++)
        {
            var fr = GetFrame((byte)(seq & 0xFF));
            queue.Enqueue(fr);
            seq = (seq + 1) & 0xFF;

            if (queue.Count >= batchSize || i == count - 1)
            {
                while (queue.Count > 0)
                {
                    int send = asyncTx
                        ? await tx.TransmitAsync(queue, 100).ConfigureAwait(false)
                        : tx.Transmit(queue, 100);

                    var overflow = send < 0;
                    if (overflow) send = -send;

                    for (var j = 0; j < send && queue.Count > 0; j++)
                        queue.Dequeue();

                    if (overflow)
                    {
                        Thread.Yield();
                        continue;
                    }
                    if (queue.Count == 0) break;
                }

                if (gapMs > 0 && i != count - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(gapMs / 1000.0));
                }
            }
        }
    }


    private static ICanFrame GetFrame(byte seq) => _seqFrames![seq];

    private static ICanFrame[] CreateFrameRing(uint baseId, bool extended, bool useFd, bool brs, int len)
    {
        var ring = new ICanFrame[256];
        var l = Math.Max(len, 1);

        for (var seq = 0; seq < 256; seq++)
        {
            var payload = new byte[l];
            payload[0] = (byte)seq;
            for (var i = 1; i < l; i++)
            {
                payload[i] = (byte)(i ^ seq);
            }

            ReadOnlyMemory<byte> mem = payload;

            var id = baseId | (uint)seq;
            ring[seq] = useFd
                ? new CanFdFrame((int)id, mem, brs) { IsExtendedFrame = extended }
                : new CanClassicFrame((int)id, mem, extended);
        }

        return ring;
    }


    private static void PrintSummary(string name, int expected, SequenceVerifier v, TimeSpan elapsed, ICanBus bus)
    {
        var output = $"[{name}] time={elapsed.TotalMilliseconds:F0}ms, expected={expected}, rx={v.Received}, loss={v.Lost}, dup={v.Duplicates}, ooo={v.OutOfOrder}, bad={v.BadData}, errs={v.ErrorFrames}";

        if (bus.Options.Features.HasFlag(CanFeature.BusUsage))
        {
            output += $"busUsage={bus.BusUsage():F1}%";
        }

        Console.WriteLine(output);
    }

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool TryGetTwoInts(string[] args, string name, out int a, out int b)
    {
        a = b = 0;
        for (var i = 0; i < args.Length - 2; i++)
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
        {
            if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int ParseInt(string? s, int def)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return def;
        }

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(2),
                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hx))
        {
            return hx;
        }

        return int.TryParse(s, out var v) ? v : def;
    }

    private static int ClampInt(int v, int min, int max)
    {
        if (v < min)
        {
            return min;
        }

        if (v > max)
        {
            return max;
        }

        return v;
    }

    private sealed class SequenceVerifier
    {
        private readonly object _lock = new();

        private int _expected;
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
                var seqFromId = fr.ID & 0xFF;
                var seqFromPayload = fr.Data.Length > 0 ? fr.Data.Span[0] : -1;
                if (seqFromPayload != seqFromId)
                {
                    BadData++;
                }

                var delta = (seqFromId - _expected) & 0xFF;
                if (delta == 0)
                {
                    _expected = (_expected + 1) & 0xFF;
                }
                else
                {
                    if (delta == 0xFF)
                    {
                        Duplicates++;
                    }
                    else if (delta < 0x80)
                    {
                        Lost += delta;
                    }
                    else
                    {
                        OutOfOrder++;
                    }

                    _expected = (seqFromId + 1) & 0xFF;
                }

                Received++;
            }
        }

        public Snapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new Snapshot(Received, Lost, Duplicates, OutOfOrder, BadData, ErrorFrames);
            }
        }

        public void RecordError(ICanErrorInfo info)
        {
            lock (_lock)
            {
                ErrorFrames++;
            }
        }

        public readonly struct Snapshot(
            int received,
            int lost,
            int duplicates,
            int outOfOrder,
            int badData,
            int errorFrames)
        {
            public readonly int received = received;
            public readonly int lost = lost;
            public readonly int duplicates = duplicates;
            public readonly int outOfOrder = outOfOrder;
            public readonly int badData = badData;
            public readonly int errorFrames = errorFrames;
        }
    }

    private sealed class ActionOnDispose(Action a) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                a();
            }
            catch
            {
                /*Ignored*/
            }
        }
    }
}
