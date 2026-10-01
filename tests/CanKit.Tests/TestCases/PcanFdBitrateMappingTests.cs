using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.PCAN;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Pure mapping tests for the PCAN FD bit timing; no driver or hardware is touched.
/// The Peak types are only used through <c>var</c>, so the file compiles against both the
/// real SDK (Debug/Release) and the in-memory double (Fake).
/// </summary>
public class PcanFdBitrateMappingTests
{
    [Fact]
    public void Data_Phase_Is_Computed_From_The_Data_Bitrate()
    {
        var timing = new CanBusTiming(new CanFdTiming(
            CanPhaseTiming.Target(500_000, 800),
            CanPhaseTiming.Target(2_000_000, 750),
            80));

        var fd = PcanUtils.MapFdBitrate(timing);

        var nominal = fd.Nominal;
        Rate(nominal.Brp, nominal.Tseg1, nominal.Tseg2).Should().Be(500_000);
        SamplePoint(nominal.Tseg1, nominal.Tseg2).Should().BeApproximately(0.80, 0.001);
        nominal.Mode.ToString().Should().Be("ArbitrationPhase");

        var data = fd.Data;
        Rate(data.Brp, data.Tseg1, data.Tseg2).Should().Be(2_000_000);
        SamplePoint(data.Tseg1, data.Tseg2).Should().BeApproximately(0.75, 0.001);
        data.Mode.ToString().Should().Be("DataPhase");
    }

    [Theory]
    [InlineData(1_000_000u, (ushort)800)]
    [InlineData(2_000_000u, (ushort)800)]
    [InlineData(2_000_000u, (ushort)875)]
    [InlineData(4_000_000u, (ushort)800)]
    [InlineData(5_000_000u, (ushort)750)]
    [InlineData(8_000_000u, (ushort)800)]
    public void Data_Phase_Stays_Within_The_PCAN_Data_Segment_Ranges(uint dataBitrate, ushort samplePointPermille)
    {
        var timing = new CanBusTiming(new CanFdTiming(
            CanPhaseTiming.Target(500_000, 800),
            CanPhaseTiming.Target(dataBitrate, samplePointPermille),
            80));

        var data = PcanUtils.MapFdBitrate(timing).Data;

        Rate(data.Brp, data.Tseg1, data.Tseg2).Should().Be(dataBitrate);
        ((int)data.Tseg1).Should().BeInRange(1, 32);
        ((int)data.Tseg2).Should().BeInRange(1, 16);
        ((int)data.Sjw).Should().BeInRange(1, 16);
    }

    [Theory]
    [InlineData(8_000_000u, 40u)]
    [InlineData(10_000_000u, 40u)]
    public void Data_Phase_Can_Use_Fewer_Than_Eight_Quanta_When_The_Clock_Requires_It(uint dataBitrate, uint clockMHz)
    {
        // 8 Mbit/s on a 40 MHz clock only fits 5 time quanta: BRP=1, TSEG1=2, TSEG2=2 at 60 %.
        var timing = new CanBusTiming(new CanFdTiming(
            CanPhaseTiming.Target(500_000, 800),
            CanPhaseTiming.Target(dataBitrate, 800),
            clockMHz));

        var data = PcanUtils.MapFdBitrate(timing).Data;

        Rate(data.Brp, data.Tseg1, data.Tseg2, clockMHz * 1_000_000.0).Should().Be(dataBitrate);
        (1 + (int)data.Tseg1 + (int)data.Tseg2).Should().BeLessThan(8);
        ((int)data.Tseg1).Should().BeInRange(1, 32);
        ((int)data.Tseg2).Should().BeInRange(1, 16);
    }

    [Theory]
    [InlineData(1_000_000u)]
    [InlineData(2_000_000u)]
    [InlineData(4_000_000u)]
    [InlineData(5_000_000u)]
    public void Data_Phase_Keeps_At_Least_Eight_Quanta_Where_The_Clock_Allows_It(uint dataBitrate)
    {
        var timing = new CanBusTiming(new CanFdTiming(
            CanPhaseTiming.Target(500_000, 800),
            CanPhaseTiming.Target(dataBitrate, 800),
            80));

        var data = PcanUtils.MapFdBitrate(timing).Data;

        Rate(data.Brp, data.Tseg1, data.Tseg2).Should().Be(dataBitrate);
        (1 + (int)data.Tseg1 + (int)data.Tseg2).Should().BeGreaterThanOrEqualTo(8);
    }

    // Combinations the PCAN-Basic driver rejected on a PCAN-USB Pro FD (5.1.0.1194) with InvalidValue,
    // all of them with data_brp=1 and data_tseg2=1.
    [Theory]
    [InlineData(8_000_000u, (ushort)800, 40u)]
    [InlineData(10_000_000u, (ushort)800, 40u)]
    [InlineData(4_000_000u, (ushort)875, 40u)]
    [InlineData(8_000_000u, (ushort)875, 80u)]
    [InlineData(10_000_000u, (ushort)875, 80u)]
    [InlineData(5_000_000u, (ushort)875, 60u)]
    [InlineData(5_000_000u, (ushort)800, 30u)]
    [InlineData(4_000_000u, (ushort)800, 24u)]
    [InlineData(2_000_000u, (ushort)875, 20u)]
    [InlineData(5_000_000u, (ushort)800, 20u)]
    public void Data_Phase_Uses_Tseg2_Of_At_Least_Two_When_Brp_Is_One(uint dataBitrate, ushort samplePointPermille, uint clockMHz)
    {
        var timing = new CanBusTiming(new CanFdTiming(
            CanPhaseTiming.Target(500_000, 800),
            CanPhaseTiming.Target(dataBitrate, samplePointPermille),
            clockMHz));

        var data = PcanUtils.MapFdBitrate(timing).Data;

        Rate(data.Brp, data.Tseg1, data.Tseg2, clockMHz * 1_000_000.0).Should().Be(dataBitrate);
        if (data.Brp == 1)
            ((int)data.Tseg2).Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Data_Phase_Keeps_Tseg2_Of_One_When_Brp_Is_Above_One()
    {
        // 2 Mbit/s at 87.5 % on 80 MHz is only exact with BRP=5, TSEG1=6, TSEG2=1; the driver accepts it.
        var timing = new CanBusTiming(new CanFdTiming(
            CanPhaseTiming.Target(500_000, 800),
            CanPhaseTiming.Target(2_000_000, 875),
            80));

        var data = PcanUtils.MapFdBitrate(timing).Data;

        Rate(data.Brp, data.Tseg1, data.Tseg2).Should().Be(2_000_000);
        SamplePoint(data.Tseg1, data.Tseg2).Should().BeApproximately(0.875, 0.001);
    }

    private static double Rate(double brp, double tseg1, double tseg2, double clockHz = 80_000_000.0)
        => clockHz / (brp * (1 + tseg1 + tseg2));

    private static double SamplePoint(double tseg1, double tseg2)
        => (1 + tseg1) / (1 + tseg1 + tseg2);
}
