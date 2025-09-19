using System;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.Tests.Fakes;

namespace Pkuyo.CanKit.Net.Tests;

public class OptionsConfiguratorTests
{
    [Fact]
    public void Channel_Fd_Throws_When_Feature_Not_Supported()
    {
        var provider = new TestModelProvider(Core.Definitions.CanFeature.CanClassic);  // no FD
        var device = new TestDevice(new TestDeviceOptions { Provider = provider });
        using var session = new Pkuyo.CanKit.Net.Core.CanSession<TestDevice, TestChannel>(device, provider);
        session.Open();

        Assert.Throws<CanFeatureNotSupportedException>(() =>
            session.CreateChannel(0, cfg => cfg.Fd(500_000, 2_000_000)));
    }
     [Fact]
    public void Init_Config_Applied_To_RT_Options_For_Baud()
    {
        var provider = new TestModelProvider();
        var device = new TestDevice(new TestDeviceOptions { Provider = provider });
        using var session = new CanSession<TestDevice, TestChannel>(device, provider);
        session.Open();

        var ch = session.CreateChannel(0, cfg => cfg
            .Baud(500_000)
            .SetTxRetryPolicy(TxRetryPolicy.AlwaysRetry)
            .SetWorkMode(ChannelWorkMode.ListenOnly)
            .InternalRes(true)
            .BusUsage(200)
        );

        var rt = ch.Options;
        Assert.Equal(500_000u, rt.BitTiming.BaudRate);
        Assert.Null(rt.BitTiming.ArbitrationBitRate);
        Assert.Null(rt.BitTiming.DataBitRate);
        Assert.Equal(TxRetryPolicy.AlwaysRetry, rt.TxRetryPolicy);
        Assert.Equal(ChannelWorkMode.ListenOnly, rt.WorkMode);
        Assert.True(rt.InternalResistance);
        Assert.True(rt.BusUsageEnabled);
        Assert.Equal(200u, rt.BusUsagePeriodTime);
        Assert.Equal(CanProtocolMode.Can20, rt.ProtocolMode);
    }

    [Fact]
    public void Init_Config_Applied_For_Fd()
    {
        var provider = new TestModelProvider(CanFeature.CanClassic | CanFeature.Filters | CanFeature.ErrorCounters | CanFeature.BusUsage | CanFeature.CanFd);
        var device = new TestDevice(new TestDeviceOptions { Provider = provider });
        using var session = new CanSession<TestDevice, TestChannel>(device, provider);
        session.Open();

        var ch = session.CreateChannel(0, cfg => cfg.Fd(500_000, 2_000_000));
        var rt = ch.Options;
        Assert.Equal(500_000u, rt.BitTiming.BaudRate);
        Assert.Equal(500_000u, rt.BitTiming.ArbitrationBitRate);
        Assert.Equal(2_000_000u, rt.BitTiming.DataBitRate);
    }

    [Fact]
    public void ProtocolMode_CanFd_Sets_Default_Fd_Timing()
    {
        var provider = new TestModelProvider(CanFeature.CanClassic | CanFeature.Filters | CanFeature.ErrorCounters | CanFeature.BusUsage | CanFeature.CanFd);
        var device = new TestDevice(new TestDeviceOptions { Provider = provider });
        using var session = new CanSession<TestDevice, TestChannel>(device, provider);
        session.Open();

        var ch = session.CreateChannel(0, cfg => cfg.SetProtocolMode(CanProtocolMode.CanFd));
        var rt = ch.Options;
        Assert.Null(rt.BitTiming.BaudRate);
        Assert.Equal(500_000u, rt.BitTiming.ArbitrationBitRate);
        Assert.Equal(500_000u, rt.BitTiming.DataBitRate);
    }

    [Fact]
    public void Filters_Are_Applied_And_Shared_Object_Preserved()
    {
        var provider = new TestModelProvider();
        var device = new TestDevice(new TestDeviceOptions { Provider = provider });
        using var session = new CanSession<TestDevice, TestChannel>(device, provider);
        session.Open();

        var filter = new CanFilter();
        var ch = session.CreateChannel(0, cfg =>
            cfg.SetFilter(filter).AccMask(0x100, 0x7FF, CanFilterIDType.Standard).RangeFilter(0x200, 0x2FF, CanFilterIDType.Extend));

        Assert.Same(filter, ch.Options.Filter);
        Assert.Equal(2, ch.Options.Filter.FilterRules.Count);
    }

    [Fact]
    public void FeatureGate_BusUsage_Not_Supported_Throws()
    {
        var provider = new TestModelProvider(CanFeature.CanClassic); // no BusUsage
        var device = new TestDevice(new TestDeviceOptions { Provider = provider });
        using var session = new CanSession<TestDevice, TestChannel>(device, provider);
        session.Open();

        Assert.Throws<CanFeatureNotSupportedException>(() => session.CreateChannel(0, cfg => cfg.BusUsage(100)));
    }

    [Fact]
    public void FeatureGate_Filters_Not_Supported_Throws()
    {
        var provider = new TestModelProvider(CanFeature.CanClassic); // no Filters
        var device = new TestDevice(new TestDeviceOptions { Provider = provider });
        using var session = new CanSession<TestDevice, TestChannel>(device, provider);
        session.Open();

        Assert.Throws<CanFeatureNotSupportedException>(() => session.CreateChannel(0, cfg => cfg.SetFilter(new CanFilter())));
        Assert.Throws<CanFeatureNotSupportedException>(() => session.CreateChannel(0, cfg => cfg.RangeFilter(1, 2, CanFilterIDType.Standard)));
        Assert.Throws<CanFeatureNotSupportedException>(() => session.CreateChannel(0, cfg => cfg.AccMask(0, 0, CanFilterIDType.Extend)));
    }
}

