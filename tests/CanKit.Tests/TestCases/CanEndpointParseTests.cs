using System;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

// Unit tests for CanEndpoint.Parse. Motivated by review §2.5:
// the previous Uri-based implementation lowercased the host
// ("zlg://USBCANFD-200U" -> "usbcanfd-200u") and rejected several
// characters that adapters need to accept in device names.
public class CanEndpointParseTests
{
    [Fact]
    public void Preserves_Host_Case_For_Zlg_Device()
    {
        var ep = CanEndpoint.Parse("zlg://USBCANFD-200U?index=0#ch1");

        ep.Scheme.Should().Be("zlg");
        ep.Path.Should().Be("USBCANFD-200U");
        ep.Fragment.Should().Be("ch1");
        ep.Query.Should().ContainKey("index").WhoseValue.Should().Be("0");
        ep.Original.Should().Be("zlg://USBCANFD-200U?index=0#ch1");
    }

    [Fact]
    public void Preserves_Mixed_Case_Path_Segments()
    {
        var ep = CanEndpoint.Parse("zlg://ZCAN_USBCANFD_200U/ChannelA/Sub");

        ep.Scheme.Should().Be("zlg");
        ep.Path.Should().Be("ZCAN_USBCANFD_200U/ChannelA/Sub");
        ep.Fragment.Should().BeNull();
        ep.Query.Should().BeEmpty();
    }

    [Theory]
    [InlineData("zlg://My-Device", "My-Device")]
    [InlineData("zlg://My_Device", "My_Device")]
    [InlineData("zlg://My.Device", "My.Device")]
    [InlineData("zlg://Dev-01_A.beta", "Dev-01_A.beta")]
    public void Accepts_Common_Device_Name_Characters(string endpoint, string expectedPath)
    {
        var ep = CanEndpoint.Parse(endpoint);
        ep.Path.Should().Be(expectedPath);
    }

    [Fact]
    public void Decodes_Percent_Encoded_Host_And_Path()
    {
        var ep = CanEndpoint.Parse("zlg://My%20Device/Channel%201?name=Hello%20World#tag%2Fslash");

        ep.Scheme.Should().Be("zlg");
        ep.Path.Should().Be("My Device/Channel 1");
        ep.Query["name"].Should().Be("Hello World");
        ep.Fragment.Should().Be("tag/slash");
    }

    [Fact]
    public void Normalizes_Scheme_To_Lower_Case_And_Preserves_Host_Case()
    {
        var ep = CanEndpoint.Parse("ZLG://USBCANFD-200U");

        ep.Scheme.Should().Be("zlg");
        ep.Path.Should().Be("USBCANFD-200U");
    }

    [Fact]
    public void Parses_SocketCan_Endpoint()
    {
        var ep = CanEndpoint.Parse("socketcan://can0");

        ep.Scheme.Should().Be("socketcan");
        ep.Path.Should().Be("can0");
        ep.Query.Should().BeEmpty();
        ep.Fragment.Should().BeNull();
    }

    [Fact]
    public void Parses_Virtual_Session_And_Channel()
    {
        var ep = CanEndpoint.Parse("virtual://alpha/0");

        ep.Scheme.Should().Be("virtual");
        ep.Path.Should().Be("alpha/0");
        ep.Query.Should().BeEmpty();
        ep.Fragment.Should().BeNull();
    }

    [Fact]
    public void Parses_Multiple_Query_Values_Case_Insensitive_Keys()
    {
        var ep = CanEndpoint.Parse("zlg://Device?Index=2&Name=foo&Empty=");

        ep.Query.Should().HaveCount(3);
        ep.TryGet("index", out var idx).Should().BeTrue();
        idx.Should().Be("2");
        ep.TryGet("NAME", out var name).Should().BeTrue();
        name.Should().Be("foo");
        ep.TryGet("empty", out var empty).Should().BeTrue();
        empty.Should().Be(string.Empty);
        ep.TryGet("missing", out var missing).Should().BeFalse();
        missing.Should().BeNull();
    }

    [Fact]
    public void Fragment_May_Contain_Question_Mark_Because_It_Binds_Left()
    {
        // Only the first '#' starts the fragment; a following '?' is part of the
        // fragment, not a new query. Verifies the tokenizer's left-to-right binding.
        var ep = CanEndpoint.Parse("zlg://Dev#note?not-a-query");

        ep.Fragment.Should().Be("note?not-a-query");
        ep.Query.Should().BeEmpty();
    }

    [Fact]
    public void Trailing_Slash_Is_Trimmed_From_Path()
    {
        var ep = CanEndpoint.Parse("virtual://alpha/0/");
        ep.Path.Should().Be("alpha/0");
    }

    [Fact]
    public void Empty_Path_Yields_Host_Only()
    {
        var ep = CanEndpoint.Parse("zlg://Device/");
        ep.Path.Should().Be("Device");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_Null_Or_Empty_Endpoint(string? endpoint)
    {
        Action act = () => CanEndpoint.Parse(endpoint!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("no-scheme")]
    [InlineData("://missing-scheme")]
    [InlineData("zlg:/single-slash")]
    [InlineData("1bad://scheme")]
    [InlineData("bad scheme://x")]
    public void Rejects_Malformed_Endpoints(string endpoint)
    {
        Action act = () => CanEndpoint.Parse(endpoint);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Rejects_Missing_Host()
    {
        Action act = () => CanEndpoint.Parse("zlg:///path");
        act.Should().Throw<FormatException>();
    }
}
