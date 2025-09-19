using System;
using Pkuyo.CanKit.Net;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.Tests.Fakes;

namespace Pkuyo.CanKit.Net.Tests;

public class CanOpenAndSessionTests
{
    [Fact]
    public void Can_Open_GenericSession_OpensDevice()
    {
        using var session = Can.Open(TestDeviceTypes.Test);
        Assert.True(session.IsDeviceOpen);

        var ch = session.CreateChannel(0, cfg => cfg.Baud(500_000));
        Assert.NotNull(ch);
    }

    [Fact]
    public void Session_CreateChannel_RequiresDeviceOpen_And_Caches()
    {
        var provider = new TestModelProvider();
        var device = new TestDevice(new TestDeviceOptions { Provider = provider });
        using var session = new CanSession<TestDevice, TestChannel>(device, provider);

        Assert.False(session.IsDeviceOpen);
        Assert.Throws<CanDeviceNotOpenException>(() => session.CreateChannel(0, cfg => cfg.Baud(500_000)));

        session.Open();

        var ch1 = session.CreateChannel(0, cfg => cfg.Baud(500_000));
        var ch2 = session.CreateChannel(0, cfg => cfg.Baud(500_000));
        Assert.Same(ch1, ch2);
    }
}

