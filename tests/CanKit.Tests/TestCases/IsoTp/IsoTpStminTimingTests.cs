using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.IsoTp;
using FluentAssertions;
using Xunit;
// Alias the CanKit.Pro.IsoTp namespace root to avoid clashing with this test namespace's
// trailing "IsoTp" segment (same reason as in IsoTpChannelIntegrationTests).
using IsoTpFactory = CanKit.Pro.IsoTp.IsoTp;

namespace CanKit.Tests.TestCases.IsoTp;

/// <summary>
/// NFR-003 verification: the sender must pace Consecutive Frames according to the peer's
/// STmin flow-control value (ISO 15765-2). Before these tests existed, no test drove the
/// STmin scheduling path at all (every peer FC used STmin = 0), so a regression to
/// "STmin ignored" (which would flood slow real ECUs) would have been invisible in CI.
/// Bounds are deliberately soft (shared CI runners), following the philosophy of
/// <c>PeriodicJitterTests</c>: tight enough to catch a missing/broken pacing path, loose
/// enough not to flake under load.
/// </summary>
public class IsoTpStminTimingTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(10);

    private static string NewSession() => $"isotp-stmin-{Guid.NewGuid():N}";

    private static ICanBus OpenClassic(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    private static IsoTpChannelOptions FastOptions(TimeSpan? localStMin = null)
        => new()
        {
            UseCanFd = false,
            UsePadding = true,
            LocalBlockSize = 0,
            LocalStMin = localStMin ?? TimeSpan.Zero,
            NAs = TimeSpan.FromMilliseconds(500),
            NBs = TimeSpan.FromMilliseconds(500),
            NCr = TimeSpan.FromMilliseconds(500),
            WftMax = 10,
        };

    [Fact]
    public async Task Sender_Paces_Consecutive_Frames_According_To_Peer_Stmin()
    {
        const int stMinMs = 5;
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);
        using var snifferBus = OpenClassic(session, 2);

        using var sender = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x7E0, 0x7E8), FastOptions());
        // The receiver advertises STmin = 5 ms in its flow-control frames.
        using var receiver = IsoTpFactory.Open(busB, IsoTpEndpoint.Normal(0x7E8, 0x7E0),
            FastOptions(localStMin: TimeSpan.FromMilliseconds(stMinMs)));

        // Sniff the sender's Consecutive Frames (ID 0x7E0, PCI nibble 0x2) with a
        // monotonic Stopwatch timestamp taken in the observe callback.
        var cfTimes = new List<TimeSpan>();
        var sw = Stopwatch.StartNew();
        snifferBus.FrameObserved += (_, view) =>
        {
            if (view.CanFrame.ID != 0x7E0)
            {
                return;
            }
            var data = view.CanFrame.Data.Span;
            if (data.Length == 0 || (data[0] & 0xF0) != 0x20)
            {
                return;
            }
            lock (cfTimes)
            {
                cfTimes.Add(sw.Elapsed);
            }
        };

        // 60 bytes classic => FF carries 6, remaining 54 bytes over 8 CFs (7 bytes each)
        // => 7 CF-to-CF gaps to measure.
        var pdu = Enumerable.Range(0, 60).Select(i => (byte)(i & 0xFF)).ToArray();
        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        await sender.SendAsync(pdu);
        var got = await recvTask;
        got.Should().Equal(pdu);

        // Let the hub deliver the final CF to the sniffer as well before reading.
        await Task.Delay(100);

        List<TimeSpan> times;
        lock (cfTimes)
        {
            times = cfTimes.ToList();
        }
        times.Should().HaveCount(8, "60 bytes over classic CAN require exactly 8 CFs");

        var gaps = times.Zip(times.Skip(1), (a, b) => b - a).ToList();
        gaps.Should().HaveCount(7);
        gaps.Min().Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(3),
            "pacing must not collapse: CFs bursting back-to-back would mean STmin is ignored");
        gaps.Average(g => g.TotalMilliseconds).Should().BeGreaterThanOrEqualTo(4.5,
            $"average CF spacing must roughly reach the advertised STmin of {stMinMs} ms");
        gaps.Average(g => g.TotalMilliseconds).Should().BeLessThanOrEqualTo(100,
            "sanity bound against pathological over-pacing");
    }

    [Fact]
    public async Task Stmin_Zero_Keeps_MultiFrame_Transfer_Unpaced()
    {
        // Regression guard for the opposite direction: with STmin = 0 the transfer must
        // complete promptly (the existing round-trip tests cover correctness; this pins
        // down that the new pacing path does not inject artificial delays).
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        using var sender = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x7E0, 0x7E8), FastOptions());
        using var receiver = IsoTpFactory.Open(busB, IsoTpEndpoint.Normal(0x7E8, 0x7E0), FastOptions());

        var pdu = Enumerable.Range(0, 60).Select(i => (byte)(i & 0xFF)).ToArray();
        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        var sw = Stopwatch.StartNew();
        await sender.SendAsync(pdu);
        var got = await recvTask;
        sw.Stop();

        got.Should().Equal(pdu);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "with STmin = 0 no pacing delay may be injected (8 CFs over an in-memory bus)");
    }
}
