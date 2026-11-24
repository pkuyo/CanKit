using System;
using System.Threading;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Core.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class SoftwareBusUsageTests : IClassFixture<TestCaseProvider>
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 8)]
    [InlineData(true, 0)]
    [InlineData(true, 8)]
    public void Frame_Bit_Count_Includes_Payload_And_Dynamic_Stuffing(bool extended, int payloadLength)
    {
        var frame = CanFrame.Classic(0x123, new byte[payloadLength], extended);
        var lowerBound = (extended ? 67 : 47) + 8 * payloadLength;
        var upperBound = (extended ? 80 : 55) + 10 * payloadLength;

        SoftwareBusUsageEstimator.FrameBitCount(new CanFrameView(frame))
            .Should().BeInRange(lowerBound, upperBound);
    }

    [Fact]
    public void Standard_Frame_Uses_The_Actual_Crc_Remainder_For_Stuffing()
    {
        var frame = CanFrame.Classic(0x123, new byte[8]);

        SoftwareBusUsageEstimator.FrameBitCount(new CanFrameView(frame)).Should().Be(125);
    }

    [Fact]
    public void Remote_Frame_Dlc_Does_Not_Add_Payload_Bits()
    {
        var remote = new CanFrameView(CanFrameType.Can20, 0x123, new byte[8], FrameFlags.Rtr);

        SoftwareBusUsageEstimator.FrameBitCount(remote)
            .Should().BeInRange(47, 55, "DLC 8 requests eight bytes but transmits no payload");
    }

    [Fact]
    public void Mixed_Batch_Counts_The_Accepted_Classical_Frame()
    {
        using var bus = CanBus.Open($"virtual://{Guid.NewGuid()}/0", cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20).Baud(500_000)
            .SoftwareFeaturesFallBack(CanFeature.BusUsage));
        var frames = new[]
        {
            CanFrame.Fd(0x100, new byte[8]),
            CanFrame.Classic(0x123, new byte[8])
        };

        bus.Transmit(frames).Should().Be(0);

        bus.BusUsage().Should().Be(0);
    }

    [Fact]
    public void Estimator_Uses_A_One_Second_Sliding_Window()
    {
        var now = 0L;
        var estimator = new SoftwareBusUsageEstimator(() => now);
        var frame = CanFrame.Classic(0x123, new byte[8]);
        var bits = SoftwareBusUsageEstimator.FrameBitCount(new CanFrameView(frame));

        estimator.Observe(frame);
        now = TimeSpan.TicksPerSecond / 2;
        estimator.GetUsage(500_000).Should().BeApproximately(
            (float)(100d * bits * 2 / 500_000), 0.001f);

        now = TimeSpan.TicksPerSecond + 1;
        estimator.GetUsage(500_000).Should().Be(0);
    }

    [Fact]
    public void Fallback_Remains_Disabled_By_Default()
    {
        using var bus = CanBus.Open($"virtual://{Guid.NewGuid()}/0", cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

        var action = () => bus.BusUsage();
        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Enabled_Fallback_Counts_Accepted_Tx_And_Reports_Its_Source()
    {
        using var bus = CanBus.Open($"virtual://{Guid.NewGuid()}/0", cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20).Baud(500_000)
            .SoftwareFeaturesFallBack(CanFeature.BusUsage));

        bus.Transmit(CanFrame.Classic(0x123, new byte[8])).Should().Be(1);

        bus.BusUsage().Should().BeGreaterThan(0);
        bus.Options.Capabilities.Custom["bus_usage_source"].Should().Be("software");
    }

    [Fact]
    public void Software_Periodic_Tx_Is_Included_In_The_Estimate()
    {
        using var bus = CanBus.Open($"virtual://{Guid.NewGuid()}/0", cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20).Baud(500_000)
            .SoftwareFeaturesFallBack(CanFeature.BusUsage | CanFeature.CyclicTx));
        using var periodic = bus.TransmitPeriodic(CanFrame.Classic(0x123, new byte[8]),
            new PeriodicTxOptions(TimeSpan.FromMilliseconds(10), repeat: 1));

        SpinWait.SpinUntil(() => bus.BusUsage() > 0, TimeSpan.FromSeconds(1))
            .Should().BeTrue();
    }
}
