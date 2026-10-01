#if FAKE
using System;
using System.Collections.Generic;
using System.Linq;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Kvaser;
using CanKit.Core.Exceptions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// ListenOnly on Kvaser runs the channel with <c>canDRIVER_SILENT</c>. The FAKE models the CANlib
/// driver type: a silent channel receives, but nothing it writes reaches the other channels.
/// FAKE channel 2 reports no <c>canCHANNEL_CAP_SILENT_MODE</c>, like a Kvaser Leaf Light v2.
/// </summary>
public class KvaserListenOnlyTests
{
    [Fact]
    public void ListenOnly_Channel_Receives_But_Does_Not_Transmit()
    {
        using var listener = Kvaser.Open(0, cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20)
            .Baud(500_000)
            .SetWorkMode(ChannelWorkMode.ListenOnly));
        using var peer = Kvaser.Open(1, cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20)
            .Baud(500_000));

        peer.Transmit(CanFrame.Classic(0x100, new byte[] { 1 }));
        listener.Transmit(CanFrame.Classic(0x200, new byte[] { 2 }));

        ReceiveIds(listener).Should().Equal(0x100);
        ReceiveIds(peer).Should().BeEmpty();
    }

    [Fact]
    public void ListenOnly_Is_Rejected_On_A_Channel_Without_Silent_Mode()
    {
        // canSetBusOutputControl(canDRIVER_SILENT) returns canOK on such a device but is ignored,
        // so the channel would keep acknowledging and transmitting.
        var open = () => Kvaser.Open(2, cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20)
            .Baud(500_000)
            .SetWorkMode(ChannelWorkMode.ListenOnly));

        open.Should().Throw<CanFeatureNotSupportedException>();
    }

    [Fact]
    public void ListenOnly_Is_Rejected_When_The_Driver_Does_Not_Read_Back_Silent()
    {
        // The channel reports silent mode and accepts the call, but stays in normal mode.
        CanKit.Adapter.Kvaser.Native.Canlib.IgnoreBusOutputControl = true;
        try
        {
            var open = () => Kvaser.Open(0, cfg => cfg
                .SetProtocolMode(CanProtocolMode.Can20)
                .Baud(500_000)
                .SetWorkMode(ChannelWorkMode.ListenOnly));

            open.Should().Throw<CanBusCreationException>();
        }
        finally
        {
            CanKit.Adapter.Kvaser.Native.Canlib.IgnoreBusOutputControl = false;
        }

        // The failed open released the handle: the channel can be opened again.
        using var bus = Kvaser.Open(0, cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20)
            .Baud(500_000)
            .SetWorkMode(ChannelWorkMode.ListenOnly));
        bus.Options.WorkMode.Should().Be(ChannelWorkMode.ListenOnly);
    }

    [Fact]
    public void ListenOnly_Is_Rejected_When_The_Driver_Type_Cannot_Be_Read_Back()
    {
        // Without a readback there is no confirmation that the channel is silent.
        CanKit.Adapter.Kvaser.Native.Canlib.BusOutputControlReadbackError =
            CanKit.Adapter.Kvaser.Native.Canlib.canStatus.canERR_NOT_IMPLEMENTED;
        try
        {
            var open = () => Kvaser.Open(0, cfg => cfg
                .SetProtocolMode(CanProtocolMode.Can20)
                .Baud(500_000)
                .SetWorkMode(ChannelWorkMode.ListenOnly));

            open.Should().Throw<CanBusCreationException>();

            // Normal mode does not depend on the readback.
            using var normal = Kvaser.Open(0, cfg => cfg
                .SetProtocolMode(CanProtocolMode.Can20)
                .Baud(500_000));
        }
        finally
        {
            CanKit.Adapter.Kvaser.Native.Canlib.BusOutputControlReadbackError = null;
        }
    }

    [Fact]
    public void Normal_Mode_Opens_On_A_Channel_Without_Silent_Mode()
    {
        using var bus = Kvaser.Open(2, cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20)
            .Baud(500_000));

        bus.Options.Features.Should().NotHaveFlag(CanFeature.ListenOnly);
    }

    private static List<int> ReceiveIds(ICanBus bus)
    {
        var ids = new List<int>();
        var until = DateTime.UtcNow.AddMilliseconds(300);
        while (DateTime.UtcNow < until)
            ids.AddRange(bus.Receive(16, 50).Select(r => r.CanFrame.ID));
        return ids;
    }
}
#endif
