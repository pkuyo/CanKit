using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Tests;
using CanKit.Core.Definitions;
using CanKit.Tests.Matrix;
using CanKit.Tests.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class FilterTests : IClassFixture<TestCaseProvider>
{
    // Filter: range
    [Theory]
    [MemberData(nameof(TestMatrix.CombinedRangeFilter), MemberType = typeof(TestMatrix))]
    public async Task Filter_Range_Work(string epA, string epB, string endpoint, bool hasFd,
        ITestDataProvider.FilterRange[] range, ITestDataProvider.FilterFrame[] frame, int expected)
    {
        _ = hasFd;
        _ = endpoint;
        using var rx = Core.CanBus.Open(epA, cfg =>
        {
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate);
            cfg.SoftwareFeaturesFallBack(CanFeature.All);
            foreach(var r in range)
            {
                cfg.RangeFilter(r.Min, r.Max, (CanFilterIDType)r.Ide);
            }

            cfg.EnableErrorInfo().SetAsyncBufferCapacity(8192);
            TestCaseProvider.Provider.TestBusInitFunc?.Invoke(cfg);
        });
        using var tx = TestHelpers.OpenClassic(epB);

        var allFrames = new List<CanFrame>();
        foreach(var f in frame)
        {
            allFrames.Add(CanFrame.Classic(f.Id, new[] { (byte)(f.Id & 0xFF) }, f.Ide == 1));
        }
        await TestHelpers.SendBurstAsync(tx, allFrames, 0);

        var v = new TestHelpers.SequenceVerifier();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var rec = await TestHelpers.ReceiveUntilAsync(rx, v, expected, 2000, cts.Token);
        rec.Should().Be(expected);
    }

    // Filter: mask
    [Theory]
    [MemberData(nameof(TestMatrix.CombinedMaskFilter), MemberType = typeof(TestMatrix))]
    public async Task Filter_Mask_Work(string epA, string epB, string endpoint, bool hasFd,
        ITestDataProvider.FilterMask[] masks, ITestDataProvider.FilterFrame[] frame, int expected)
    {
        _ = hasFd;
        _ = endpoint;

        using var rx = Core.CanBus.Open(epA, cfg =>
        {
            TestCaseProvider.Provider.TestBusInitFunc?.Invoke(cfg);
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate);
            cfg.SoftwareFeaturesFallBack(CanFeature.All);
            foreach(var r in masks)
            {
                cfg.AccMask(r.AccCode, r.AccMask, (CanFilterIDType)r.Ide);
            }

            cfg.EnableErrorInfo().SetAsyncBufferCapacity(8192);
        });
        using var tx = TestHelpers.OpenClassic(epB);

        var allFrames = new List<CanFrame>();
        foreach(var f in frame)
        {
            allFrames.Add(CanFrame.Classic(f.Id, new[] { (byte)(f.Id & 0xFF) }, f.Ide == 1));
        }
        await TestHelpers.SendBurstAsync(tx, allFrames, 0);

        var v = new TestHelpers.SequenceVerifier();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var rec = await TestHelpers.ReceiveUntilAsync(rx, v, expected, 2000, cts.Token);
        rec.Should().Be(expected);
    }
}
