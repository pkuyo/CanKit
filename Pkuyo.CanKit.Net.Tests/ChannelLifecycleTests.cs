using System;
using System.Linq;
using Pkuyo.CanKit.Net;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.Tests.Fakes;

namespace Pkuyo.CanKit.Net.Tests;

public class ChannelLifecycleTests
{
    [Fact]
    public void DestroyChannel_When_NotCreated_Throws()
    {
        using var session = Can.Open(TestDeviceTypes.Test);
        Assert.True(session.IsDeviceOpen);
        Assert.Throws<CanChannelNotOpenException>(() => session.DestroyChannel(0));
    }

    [Fact]
    public void DestroyChannel_RemovesCache_AllowsRecreate_NewInstance()
    {
        using var session = Can.Open<TestDevice, TestChannel, IDeviceOptions, IDeviceInitOptionsConfigurator>(TestDeviceTypes.Test);

        var ch1 = session.CreateChannel(0, cfg => cfg.Baud(500_000));
        Assert.NotNull(ch1);
        session.DestroyChannel(0);
        var ch2 = session.CreateChannel(0, cfg => cfg.Baud(500_000));
        Assert.NotSame(ch1, ch2);
    }

    [Fact]
    public void Session_Dispose_ClosesDevice_And_ClearsChannels()
    {
        var session = Can.Open<TestDevice, TestChannel, IDeviceOptions, IDeviceInitOptionsConfigurator>(TestDeviceTypes.Test);
        var ch = session.CreateChannel(0, cfg => cfg.Baud(500_000));
        ch.Open();
        session.Dispose();

        Assert.False(session.IsDeviceOpen);
        Assert.Throws<CanDeviceNotOpenException>(() => session.CreateChannel(1, cfg => cfg.Baud(500_000)));
    }

    [Fact]
    public void Channel_BeforeOpen_Throws_On_Tx_And_Rx()
    {
        using var session = Can.Open<TestDevice, TestChannel, IDeviceOptions, IDeviceInitOptionsConfigurator>(TestDeviceTypes.Test);
        var ch = session.CreateChannel(0, cfg => cfg.Baud(500_000));
        Assert.Throws<CanChannelNotOpenException>(() => ch.Transmit(new CanClassicFrame(rawIDInit: 0x100)));
        Assert.Throws<CanChannelNotOpenException>(() => ch.Receive(1).ToArray());
    }

    [Fact]
    public void Channel_Open_Tx_Rx_And_FrameReceived_Event()
    {
        // Use provider with exposed transceiver to feed RX frames
        var provider = new ExposedTransceiverProvider();

        var device = new TestDevice(new TestDeviceOptions { Provider = provider });
        using var session = new CanSession<TestDevice, TestChannel>(device, provider);
        session.Open();

        var ch = session.CreateChannel(0, cfg => cfg.Baud(500_000));
        ch.Open();

        var sent = ch.Transmit(new CanClassicFrame(rawIDInit: 0x101), new CanClassicFrame(rawIDInit: 0x102));
        Assert.Equal(2u, sent);

        int receivedCount = 0;
        ch.FrameReceived += (_, __) => receivedCount++;

        var exp = provider.ExposedFactory.Transceiver;
        exp.ToReceive.Add(new CanReceiveData(new CanClassicFrame(rawIDInit: 0x201)));
        exp.ToReceive.Add(new CanReceiveData(new CanClassicFrame(rawIDInit: 0x202)));

        var rx = ch.Receive(10).ToArray();
        Assert.Equal(2, rx.Length);
        Assert.Equal(2, receivedCount);
    }
}
