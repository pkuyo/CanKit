using CanKit.Abstractions.API.Common;
using CanKit.Adapter.Virtual;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Exercises the configurator through <see cref="IBusInitOptionsConfigurator"/>, which is the
/// surface <c>CanBus.Open(endpoint, cfg =&gt; ...)</c> hands to callers.
/// </summary>
public class BusInitOptionsConfiguratorTests
{
    [Fact]
    public void Baud_Through_Interface_Keeps_Clock_And_SamplePoint_Apart()
    {
        var (options, cfg) = new VirtualProvider().GetChannelOptions();

        cfg.Baud(500_000, clockMHz: 80, samplePointPermille: 875);

        var classic = options.BitTiming.Classic!.Value;
        classic.clockMHz.Should().Be(80u);
        classic.Nominal.Bitrate.Should().Be(500_000u);
        classic.Nominal.SamplePointPermille.Should().Be((ushort)875);
    }

    [Fact]
    public void Baud_Through_Interface_Without_Clock_Leaves_Clock_Unset()
    {
        var (options, cfg) = new VirtualProvider().GetChannelOptions();

        cfg.Baud(250_000, samplePointPermille: 800);

        var classic = options.BitTiming.Classic!.Value;
        classic.clockMHz.Should().BeNull();
        classic.Nominal.SamplePointPermille.Should().Be((ushort)800);
    }

    [Fact]
    public void Fd_Through_Interface_Keeps_Clock_And_Both_SamplePoints_Apart()
    {
        var (options, cfg) = new VirtualProvider().GetChannelOptions();

        cfg.Fd(500_000, 2_000_000, clockMHz: 80, nominalSamplePointPermille: 800, dataSamplePointPermille: 750);

        var fd = options.BitTiming.Fd!.Value;
        fd.clockMHz.Should().Be(80u);
        fd.Nominal.Bitrate.Should().Be(500_000u);
        fd.Nominal.SamplePointPermille.Should().Be((ushort)800);
        fd.Data.Bitrate.Should().Be(2_000_000u);
        fd.Data.SamplePointPermille.Should().Be((ushort)750);
    }
}
