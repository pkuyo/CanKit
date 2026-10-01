#if FAKE
using System;
using System.Linq;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Kvaser;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Kvaser hardware periodic TX (object buffer) with a finite <see cref="PeriodicTxOptions.Repeat"/>
/// sends <c>Repeat</c> frames in total, like the SocketCAN BCM and the software scheduler.
/// The FAKE models <c>canObjBufSetMsgCount</c> from the CANlib documentation: the buffer sends
/// <c>count</c> frames and resets the count to 0 (infinite) afterwards.
/// </summary>
public class KvaserPeriodicRepeatTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(10);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Finite_Repeat_Sends_Exactly_Repeat_Frames(bool fireImmediately)
    {
        using var tx = Open(0);
        using var rx = Open(1);

        using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, 3, fireImmediately));

        Count(rx, TimeSpan.FromMilliseconds(400)).Should().Be(3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Repeat_Zero_Sends_Nothing(bool fireImmediately)
    {
        using var tx = Open(0);
        using var rx = Open(1);

        using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, 0, fireImmediately));

        Count(rx, TimeSpan.FromMilliseconds(200)).Should().Be(0);
    }

    [Fact]
    public void Infinite_Repeat_Keeps_Sending()
    {
        using var tx = Open(0);
        using var rx = Open(1);

        using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, -1));

        Count(rx, TimeSpan.FromMilliseconds(300)).Should().BeGreaterThan(10);
    }

    [Fact]
    public void Update_After_Completion_Sends_The_New_Count_Only()
    {
        using var tx = Open(0);
        using var rx = Open(1);

        using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, 3));
        Count(rx, TimeSpan.FromMilliseconds(400)).Should().Be(3);

        periodic.Update(repeatCount: 2);

        Count(rx, TimeSpan.FromMilliseconds(400)).Should().Be(2);
    }

    private static ICanBus Open(int channel)
        => Kvaser.Open(channel, cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

    private static CanFrame Frame() => CanFrame.Classic(0x6A0, new byte[] { 0xAA });

    private static int Count(ICanBus bus, TimeSpan window)
    {
        var count = 0;
        var until = DateTime.UtcNow + window;
        while (DateTime.UtcNow < until)
            count += bus.Receive(64, 20).Count(r => r.CanFrame.ID == 0x6A0);
        return count;
    }
}
#endif
