#if FAKE
using System;
using CanKit.Adapter.SocketCAN;
using CanKit.Adapter.SocketCAN.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

[Trait("Category", "FakeOnly")]
public class SocketCanCapabilityTests
{
    [Fact]
    public void QueryCapabilities_Uses_Static_Features_When_Existing_Interface_Has_No_Ctrlmode()
    {
        var provider = new SocketCanProvider();
        var options = new SocketCanBusOptions(provider)
        {
            ChannelName = "vcan3"
        };

        var capabilities = provider.QueryCapabilities(options);

        capabilities.Features.Should().Be(provider.StaticFeatures);
    }

    [Fact]
    public void QueryCapabilities_Throws_When_Interface_Does_Not_Exist()
    {
        var provider = new SocketCanProvider();
        var options = new SocketCanBusOptions(provider)
        {
            ChannelName = "missing-can-interface"
        };

        Action query = () => provider.QueryCapabilities(options);

        query.Should().Throw<SocketCanNativeException>();
    }

    [Fact]
    public void QueryCapabilities_Throws_When_Existing_Interface_Has_A_Native_Error()
    {
        var provider = new SocketCanProvider();
        var options = new SocketCanBusOptions(provider)
        {
            ChannelName = "vcan4"
        };

        Action query = () => provider.QueryCapabilities(options);

        query.Should().Throw<SocketCanNativeException>()
            .Which.NativeErrorCode.Should().Be(13U);
    }
}
#endif
