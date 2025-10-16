using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests;

public class ThroughputAndFeaturesTests : IClassFixture<TestAssemblyLoader>
{
    // 64 and 128 one-shot batch (Classic)
    // 单次64和128的发送批次（CAN2.0）
    [Theory]
    [MemberData(nameof(TestMatrix.CombinedOneShotClassic), MemberType = typeof(TestMatrix))]
    public async Task OneShot_Batch_Classic_64_And_128(string epA, string epB, string endpoint, bool hasFd,
        int len, bool rtr, bool ide)
    {
        _ = hasFd;
        _ = endpoint;
        using var rx = TestHelpers.OpenClassic(epA);
        using var tx = TestHelpers.OpenClassic(epB);

        var re = TestMatrix.Pairs().ToArray();
        var ring = TestHelpers.CreateClassicSeq(0x100, ide, rtr, len);
        foreach (var batchSize in new[] { 64, 128 })
        {
            var frames = TestHelpers.GenerateSeqFrames(ring, batchSize).ToArray();
            var v = new TestHelpers.SequenceVerifier();

            await TestHelpers.SendBurstAsync(tx, frames, gapMs: 0);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var rec = await TestHelpers.ReceiveUntilAsync(rx, v, batchSize, 2000, cts.Token);
            rec.Should().Be(batchSize);
            v.Lost.Should().Be(0);
            v.BadData.Should().Be(0);
        }
    }
    // 64 and 128 one-shot batch (FD)
    // 单次64和128的发送批次（CANFD）
    [Theory]
    [MemberData(nameof(TestMatrix.CombinedOneShotFD), MemberType = typeof(TestMatrix))]
    public async Task OneShot_Batch_FD_64_And_128(string epA, string epB, string _ ,bool hasFd,
        int len, bool brs, bool ide)
    {
        if (!hasFd)
            return;
        using var rx = TestHelpers.OpenFd(epA);
        using var tx = TestHelpers.OpenFd(epB);

        var ring = TestHelpers.CreateFdSeq(0x100, ide, brs, len);
        foreach (var batchSize in new[] { 64, 128 })
        {
            var frames = TestHelpers.GenerateSeqFrames(ring, batchSize).ToArray();
            var v = new TestHelpers.SequenceVerifier();

            await TestHelpers.SendBurstAsync(tx, frames, gapMs: 0);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var rec = await TestHelpers.ReceiveUntilAsync(rx, v, batchSize, 2000, cts.Token);

            rec.Should().Be(batchSize);
            v.Lost.Should().Be(0);
            v.BadData.Should().Be(0);
        }
    }

    // >5000 continuous (CAN FD frame), gap variants with loss thresholds
    // 连续发送 > 5000帧 CANFD包，使用间隔时间
    [Theory]
    [MemberData(nameof(TestMatrix.CombinedContinuosFD), MemberType = typeof(TestMatrix))]
    public async Task Continuous_Fd_Over5000_With_Gap_And_Loss
        (string epA, string epB, string _,bool hasFd, int gapMs, double lossLimit, int len, bool brs, bool ide)
    {
        if (!hasFd)
            return;
        var count = 6000; // > 5000
        using var rx = TestHelpers.OpenFd(epA);
        using var tx = TestHelpers.OpenFd(epB);


        var ring = TestHelpers.CreateFdSeq(0x18DAF100, ide, brs, len);
        var frames = TestHelpers.GenerateSeqFrames(ring, count).ToArray();

        var v = new TestHelpers.SequenceVerifier();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;
        // Receiver loop
        var recTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && v.Received < count)
            {
                var batch = await rx.ReceiveAsync(Math.Min(512, count - v.Received), 20, ct);
                foreach (var d in batch) v.Feed(d.CanFrame);
            }
        }, cts.Token);

        await TestHelpers.SendBurstAsync(tx, frames, gapMs);

        // allow drain
        await Task.Delay(1000);
        cts.Cancel();
        try { await recTask; } catch { /* ignore */ }

        var lossRate = v.Lost / (double)count;
        lossRate.Should().BeLessThanOrEqualTo(lossLimit);
        v.BadData.Should().Be(0);
    }

    // >5000 continuous (CAN classic frame), gap variants with loss thresholds
    // 连续发送 > 5000帧 CAN包，使用间隔时间
    [Theory]
    [MemberData(nameof(TestMatrix.CombinedContinuosClassic), MemberType = typeof(TestMatrix))]
    public async Task Continuous_Classic_Over5000_With_Gap_And_Loss
        (string epA, string epB, string _, bool hasFd, int gapMs, double lossLimit, int len, bool rtr, bool ide)
    {
        if (!hasFd)
            return;

        var count = 6000; // > 5000
        using var rx = TestHelpers.OpenClassic(epA);
        using var tx = TestHelpers.OpenClassic(epB);

        var ring = TestHelpers.CreateClassicSeq(0x18DAF100, ide, rtr, len);
        var frames = TestHelpers.GenerateSeqFrames(ring, count).ToArray();

        var v = new TestHelpers.SequenceVerifier();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Receiver loop
        var recTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested && v.Received < count)
            {
                var batch = await rx.ReceiveAsync(Math.Min(512, count - v.Received), 20, cts.Token).ConfigureAwait(false);
                foreach (var d in batch) v.Feed(d.CanFrame);
            }
        }, cts.Token);

        await TestHelpers.SendBurstAsync(tx, frames, gapMs);

        // allow drain
        await Task.Delay(1000);
        cts.Cancel();
        try { await recTask; } catch { /* ignore */ }

        var lossRate = v.Lost / (double)count;
        lossRate.Should().BeLessThanOrEqualTo(lossLimit);
        v.BadData.Should().BeLessThanOrEqualTo(1);
    }
    // Frame forms: classic std/ext 0 and 8 bytes; classic remote 0 and 8; FD ext 48 and 64
    [Theory]
    [MemberData(nameof(TestMatrix.Pairs), MemberType = typeof(TestMatrix))]
    public async Task Frame_Types_And_Lengths_Are_Transferred(string epA, string epB, string _, bool hasFd)
    {
        {
            using var rxClassic = TestHelpers.OpenClassic(epA);
            using var txClassic = TestHelpers.OpenClassic(epB);

            // classic std/ext 0 and 8
            var classicCases = new List<ICanFrame>
            {
                new CanClassicFrame(0x120, ReadOnlyMemory<byte>.Empty, false),
                new CanClassicFrame(0x121, new byte[8], false),
                new CanClassicFrame(0x18DAF101, ReadOnlyMemory<byte>.Empty, true),
                new CanClassicFrame(0x18DAF102, new byte[8], true)
            };

            // classic remote (RTR) 0 and 8
            classicCases.Add(new CanClassicFrame(0x200, ReadOnlyMemory<byte>.Empty, false, true));
            classicCases.Add(new CanClassicFrame(0x201, new byte[8], false, true));

            await TestHelpers.SendBurstAsync(txClassic, classicCases, gapMs: 0);

            var recClassic = 0;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            {
                recClassic = await TestHelpers.ReceiveUntilAsync(rxClassic, new TestHelpers.SequenceVerifier(),
                    classicCases.Count, 2000, cts.Token);
            }

            recClassic.Should().Be(classicCases.Count);
        }
        if(hasFd)
        {
            // FD ext 48 and 64
            using var rxFd = TestHelpers.OpenFd(epA);
            using var txFd = TestHelpers.OpenFd(epB);

            var fdCases = new List<ICanFrame>
            {
                new CanFdFrame(0x18DAF110, new byte[48], true, false, true),
                new CanFdFrame(0x18DAF111, new byte[64], true, false, true)
            };

            await TestHelpers.SendBurstAsync(txFd, fdCases, gapMs: 0);
            var recFd = 0;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            {
                recFd = await TestHelpers.ReceiveUntilAsync(rxFd, new TestHelpers.SequenceVerifier(), fdCases.Count,
                    2000, cts.Token);
            }

            recFd.Should().Be(fdCases.Count);
        }
    }

    // Filter: range
    [Theory]
    [MemberData(nameof(TestMatrix.Pairs), MemberType = typeof(TestMatrix))]
    public async Task Filter_Range_Work(string epA, string epB, string endpoint, bool hasFd)
    {
        _ = hasFd;
        _ = endpoint;
        var rangeFilter = new CanFilter();
        rangeFilter.filterRules.Add(new FilterRule.Range(0x300, 0x30F, CanFilterIDType.Standard));

        using var rx = CanKit.Core.CanBus.Open(epA, cfg =>
        {
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000);
            cfg.SoftwareFeaturesFallBack(CanFeature.All);
            cfg.SetFilter(rangeFilter);
            cfg.EnableErrorInfo().SetAsyncBufferCapacity(8192).SetReceiveLoopStopDelayMs(200);
        });
        using var tx = TestHelpers.OpenClassic(epB);

        var allFrames = new List<ICanFrame>();
        for (int i = 0x2F0; i < 0x320; i++)
        {
            allFrames.Add(new CanClassicFrame(i, new byte[1] { (byte)(i & 0xFF) }, isExtendedFrame: false));
        }
        await TestHelpers.SendBurstAsync(tx, allFrames, 0);

        var v = new TestHelpers.SequenceVerifier();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var expected = 0x30F - 0x300 + 1;
        var rec = await TestHelpers.ReceiveUntilAsync(rx, v, expected, 2000, cts.Token);
        rec.Should().Be(expected);
    }
}

