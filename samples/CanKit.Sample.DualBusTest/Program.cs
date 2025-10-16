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
    private static int _batchSize;
    private static int _gapMs;
    private static int _rxTimeout;
    private static bool _verbose;
    private static int _count;
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

        var bitrate = ParseInt(GetArg(args, "--bitrate"), DefaultClassicBitrate);
        var abit = ParseInt(GetArg(args, "--abit"), DefaultFdArbBitrate);
        var dbit = ParseInt(GetArg(args, "--dbit"), DefaultFdDataBitrate);

        var frameLen = ParseInt(GetArg(args, "--len"), useFd ? 64 : 8);

        var asyncBuf = ParseInt(GetArg(args, "--asyncbuf"), 8192);
        var enableRes = ParseInt(GetArg(args, "--res"), 0) == 1;

        // Filtering config (optional)
        var hasMask = TryGetTwoInts(args, "--mask", out var accCode, out var accMask);
        var hasRange = TryGetTwoInts(args, "--range", out var rangeMin, out var rangeMax);

        _batchSize = ClampInt(ParseInt(GetArg(args, "--batch"), 64), 1, 4096);
        _gapMs = ParseInt(GetArg(args, "--gapms"), 0);
        _rxTimeout = ParseInt(GetArg(args, "--timeout"), 1000);
        _verbose = !HasFlag(args, "--quiet");
        _count = ParseInt(GetArg(args, "--count"), 2000);

        // Base ID (low 8 bits reserved for seq 0..255)
        var defaultBaseIdStd = 0x100; // keep low 8 bits zero
        var defaultBaseIdExt = 0x18DAF100; // keep low 8 bits zero
        var baseId = (uint)ParseInt(GetArg(args, "--baseid"), extended ? defaultBaseIdExt : defaultBaseIdStd);
        baseId &= extended ? 0x1FFFFF00u : 0x00000700u; // clear low 8 bits according to frame type


        // Which tests to run
        var mode = (GetArg(args, "--mode") ?? "all").ToLowerInvariant();

        if (_verbose)
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

        var cfgFunc = (IBusInitOptionsConfigurator cfg) =>
        {
            if (useFd)
            {
                cfg.Fd(abit, dbit).SetProtocolMode(CanProtocolMode.CanFd).InternalRes(enableRes);
            }
            else
            {
                cfg.Baud(bitrate).SetProtocolMode(CanProtocolMode.Can20).InternalRes(enableRes);
            }

            cfg.EnableErrorInfo().SetAsyncBufferCapacity(asyncBuf).SetReceiveLoopStopDelayMs(200);
            if (softFilter)
            {
                cfg.SoftwareFeaturesFallBack(CanFeature.Filters);
            }
        };
        using var busA = OpenBus(epA, cfgFunc);
        using var busB = OpenBus(epB, cfgFunc);

        _seqFrames = CreateFrameRing(baseId, extended, useFd, brs, frameLen);

        // Decide which tests to run
        var all = string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase);
        var toRun = new List<Func<ICanBus, ICanBus, Task>>();

        if (all || mode.Equals("tx-sync", StringComparison.OrdinalIgnoreCase))
        {
            toRun.Add((a, b) => Test_DUT_Tx(a, b, asyncTx: false));
        }

        if (all || mode.Equals("tx-async", StringComparison.OrdinalIgnoreCase))
        {
            toRun.Add((a, b) => Test_DUT_Tx(a, b, asyncTx: true));
        }

        if (all || mode.Equals("rx-sync", StringComparison.OrdinalIgnoreCase))
        {
            toRun.Add((a, b) => Test_DUT_Rx(a, b, false));
        }

        if (all || mode.Equals("rx-async", StringComparison.OrdinalIgnoreCase))
        {
            toRun.Add((a, b) => Test_DUT_Rx(a, b, true));
        }

        if (mode.Equals("event", StringComparison.OrdinalIgnoreCase))
        {
            toRun.Add((a, b) => Test_DUT_Rx_Event(a, b));
        }

        foreach (var r in toRun)
        {
            await r(busA, busB);
        }

        if (_verbose)
        {
            Console.WriteLine("All selected tests completed.");
        }

        return 0;
    }

    private static ICanBus OpenBus(string endpoint, Action<IBusInitOptionsConfigurator>? configure) =>
        CanBus.Open(endpoint, configure);

    // Test: DUT(B) sending many frames; tester(A) receives and verifies
    private static async Task Test_DUT_Tx(ICanBus testerReceiver, ICanBus dutSender, bool asyncTx)
    {
        var testName = asyncTx ? "DUT TX async" : "DUT TX sync";
        if (_verbose)
        {
            Console.WriteLine($"== {testName} :: Send {_count} frames, recv verify on tester ==");
        }

        var verifier = new SequenceVerifier();
        using var onErr = SubscribeError(testerReceiver, verifier);

        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var lastReceived = sw.ElapsedTicks;
        var hasReceived = false;
        var recTask = Task.Run(async () =>
        {
            while (verifier.Received < _count && !token.IsCancellationRequested)
            {
                var batch =
                    await testerReceiver.ReceiveAsync(Math.Min(256, _count - verifier.Received), 10, token).ConfigureAwait(false);
                foreach (var d in batch)
                {
                    hasReceived = true;
                    verifier.Feed(d.CanFrame);
                    Interlocked.Exchange(ref lastReceived, sw.ElapsedTicks);
                }

                if (!hasReceived)
                {
                    Interlocked.Exchange(ref lastReceived, sw.ElapsedTicks);
                }
            }
        });

        await SendBurst(dutSender, asyncTx);

        await Task.Delay(_rxTimeout);
        cts.Cancel();

        try { await recTask; }
        catch (OperationCanceledException) { }

        sw.Stop();
        PrintSummary(testName, _count, verifier, TimeSpan.FromTicks(lastReceived), testerReceiver);
    }

    private static async Task Test_DUT_Rx(ICanBus testerSender, ICanBus dutReceiver, bool asyncRx)
    {
        var testName = asyncRx ? "DUT RX async" : "DUT RX sync";
        if (_verbose)
        {
            Console.WriteLine($"== {testName} :: Send {_count} frames from tester, verify on DUT ==");
        }

        var verifier = new SequenceVerifier();
        using var onErr = SubscribeError(dutReceiver, verifier);

        var sw = Stopwatch.StartNew();

        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var lastReceived = sw.ElapsedTicks;
        var recTask = Task.Run(async () =>
        {
            if (asyncRx)
            {
                while (verifier.Received < _count && !token.IsCancellationRequested)
                {
                    var list = await dutReceiver
                        .ReceiveAsync(Math.Min(256, _count - verifier.Received), 10)
                        .ConfigureAwait(false);
                    foreach (var d in list)
                    {
                        verifier.Feed(d.CanFrame);
                        Interlocked.Exchange(ref lastReceived, sw.ElapsedTicks);
                    }
                }
            }
            else
            {
                // Using synchronous Receive
                while (verifier.Received < _count && !token.IsCancellationRequested)
                {
                    foreach (var d in dutReceiver.Receive(Math.Min(256, _count - verifier.Received), 10))
                    {
                        verifier.Feed(d.CanFrame);
                        Interlocked.Exchange(ref lastReceived, sw.ElapsedTicks);
                    }
                }
            }
        }, cts.Token);

        await SendBurst(testerSender, true);
        await Task.Delay(_rxTimeout);
        cts.Cancel();

        try { await recTask; }
        catch (OperationCanceledException) { /* ignore */ }

        PrintSummary(testName, _count, verifier, TimeSpan.FromTicks(lastReceived), dutReceiver);
    }

    // Test: DUT(B) receiving via event handler only; tester(A) sends
    private static async Task Test_DUT_Rx_Event(ICanBus testerSender, ICanBus dutReceiver)
    {
        const string testName = "DUT RX event";
        if (_verbose)
        {
            Console.WriteLine($"== {testName} :: Send {_count} frames from tester, verify on DUT(event) ==");
        }

        var verifier = new SequenceVerifier();
        using var onErr = SubscribeError(dutReceiver, verifier);

        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var lastReceived = sw.Elapsed.Ticks;

        var recTask = Task.Run(async () =>
        {
            while (verifier.Received < _count && !token.IsCancellationRequested)
            {
                var batch = await dutReceiver
                    .ReceiveAsync(Math.Min(256, _count - verifier.Received), 10, token).ConfigureAwait(false);
                foreach (var d in batch)
                {
                    verifier.Feed(d.CanFrame);
                    Interlocked.Exchange(ref lastReceived, sw.Elapsed.Ticks);
                }
            }
        });
        await SendBurst(testerSender, true);
        await Task.Delay(_rxTimeout);
        cts.Cancel();

        try { await recTask; }
        catch (OperationCanceledException) { /* ignore */ }

        sw.Stop();
        PrintSummary(testName, _count, verifier, TimeSpan.FromTicks(lastReceived), dutReceiver);
    }

    private static IDisposable SubscribeError(ICanBus bus, SequenceVerifier verifier)
    {
        EventHandler<ICanErrorInfo> onErr = (_, info) => { verifier.RecordError(info); };
        bus.ErrorFrameReceived += onErr;
        return new ActionOnDispose(() => bus.ErrorFrameReceived -= onErr);
    }

    private static async Task SendBurst(ICanBus tx, bool asyncTx)
    {
        var seq = 0;
        var queue = new Queue<ICanFrame>(_batchSize);

        for (var i = 0; i < _count; i++)
        {
            var fr = GetFrame((byte)(seq & 0xFF));
            queue.Enqueue(fr);
            seq = (seq + 1) & 0xFF;

            if (queue.Count >= _batchSize || i == _count - 1)
            {
                while (queue.Count > 0)
                {
                    int send = asyncTx
                        ? await tx.TransmitAsync(queue, 20).ConfigureAwait(false)
                        : tx.Transmit(queue, 20);

                    for (var j = 0; j < send && queue.Count > 0; j++)
                        queue.Dequeue();
                    if (queue.Count == 0)
                    {
                        if (_gapMs > 0 && i != _count - 1)
                            await Task.Delay(_gapMs);
                        break;
                    }

                    await Task.Delay(1);
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
