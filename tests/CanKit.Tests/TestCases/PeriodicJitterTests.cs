using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace CanKit.Tests.TestCases;

public class PeriodicJitterTests : IClassFixture<TestCaseProvider>
{
    private const int FrameId = 0x321;
    private const int SampleCount = 601;
    private const double AcceptanceTargetP99JitterMs = 1.0;
    private const double CiSoftP99JitterBoundMs = 5.0;
    private const int DefaultBitRate = 1_000_000;
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(5);
    private readonly ITestOutputHelper _output;

    public PeriodicJitterTests(TestCaseProvider fixture, ITestOutputHelper output)
    {
        _ = fixture;
        _output = output;
    }

    [Fact]
    public async Task SoftwarePeriodicTx_VirtualAdapter_Reports_Bounded_P99_Jitter()
    {
        var session = $"jitter-{Guid.NewGuid():N}";
        using var tx = Open(session, 0);
        using var rx = Open(session, 1);

        var timestamps = new List<long>(SampleCount);
        var gate = new object();
        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        rx.FrameObserved += (_, data) =>
        {
            if (data.CanFrame.ID != FrameId)
            {
                return;
            }

            lock (gate)
            {
                timestamps.Add(Stopwatch.GetTimestamp());
                if (timestamps.Count >= SampleCount)
                {
                    received.TrySetResult(true);
                }
            }
        };

        using var handle = tx.TransmitPeriodic(
            CanFrame.Classic(FrameId, new byte[] { 0x01 }),
            new PeriodicTxOptions(Period, SampleCount, fireImmediately: false));

        await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        handle.Stop();

        long[] snapshot;
        lock (gate)
        {
            snapshot = timestamps.Take(SampleCount).ToArray();
        }

        snapshot.Length.Should().Be(SampleCount);
        var jitter = AbsoluteIntervalJitterMs(snapshot, Period.TotalMilliseconds);
        jitter.Length.Should().BeGreaterOrEqualTo(500);

        var p99 = PercentileNearestRank(jitter, 0.99);
        var max = jitter.Max();
        var intervals = IntervalsMs(snapshot);
        var averageInterval = intervals.Average();

        _output.WriteLine(
            $"SoftwarePeriodicTx Virtual jitter: period={Period.TotalMilliseconds:F3} ms, " +
            $"samples={snapshot.Length}, intervals={jitter.Length}, avgInterval={averageInterval:F3} ms, " +
            $"p99AbsJitter={p99:F3} ms, maxAbsJitter={max:F3} ms, " +
            $"acceptanceTargetP99={AcceptanceTargetP99JitterMs:F3} ms, ciSoftBoundP99={CiSoftP99JitterBoundMs:F3} ms.");
        _output.WriteLine(Histogram(jitter));

        // The SRS acceptance target is measured on idle reference hosts. Shared CI runners can
        // have scheduler stalls, so this synthetic Virtual test logs the 1 ms target and enforces
        // only a generous software-timing bound.
        p99.Should().BeLessThanOrEqualTo(CiSoftP99JitterBoundMs);
    }

    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20)
            .Baud(TestCaseProvider.AbitRate > 0 ? TestCaseProvider.AbitRate : DefaultBitRate)
            .SoftwareFeaturesFallBack(CanFeature.All));

    private static double[] IntervalsMs(IReadOnlyList<long> timestamps)
    {
        var intervals = new double[timestamps.Count - 1];
        for (var i = 1; i < timestamps.Count; i++)
        {
            intervals[i - 1] = ToMilliseconds(timestamps[i] - timestamps[i - 1]);
        }

        return intervals;
    }

    private static double[] AbsoluteIntervalJitterMs(IReadOnlyList<long> timestamps, double expectedMs)
        => IntervalsMs(timestamps).Select(interval => Math.Abs(interval - expectedMs)).ToArray();

    private static double PercentileNearestRank(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        var rank = Math.Ceiling(percentile * sorted.Length);
        var index = Math.Min(sorted.Length - 1, Math.Max(0, (int)rank - 1));
        return sorted[index];
    }

    private static double ToMilliseconds(long stopwatchTicks)
        => stopwatchTicks * 1000.0 / Stopwatch.Frequency;

    private static string Histogram(IReadOnlyCollection<double> jitter)
    {
        var le05 = jitter.Count(value => value <= 0.5);
        var le10 = jitter.Count(value => value > 0.5 && value <= 1.0);
        var le20 = jitter.Count(value => value > 1.0 && value <= 2.0);
        var le50 = jitter.Count(value => value > 2.0 && value <= 5.0);
        var gt50 = jitter.Count(value => value > 5.0);

        return $"Abs jitter histogram: <=0.5ms={le05}, (0.5,1.0]ms={le10}, " +
            $"(1.0,2.0]ms={le20}, (2.0,5.0]ms={le50}, >5.0ms={gt50}.";
    }
}
