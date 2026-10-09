#if FAKE
using System;
using System.Linq;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Kvaser;
using CanKit.Adapter.Kvaser.Native;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;
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

    [Fact]
    public void Update_To_Infinite_During_A_Finite_Run_Keeps_Sending()
    {
        using var tx = Open(0);
        using var rx = Open(1);

        using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, 5));
        periodic.Update(repeatCount: -1);

        // A finite count left in the buffer would stop it after a few frames.
        Count(rx, TimeSpan.FromMilliseconds(400)).Should().BeGreaterThan(15);
    }

    [Fact]
    public void Update_Of_The_Frame_Keeps_The_Remaining_Count()
    {
        using var tx = Open(0);
        using var rx = Open(1);
        // A long period keeps the test independent of scheduling delays on a busy machine:
        // the update has to land somewhere inside the run, not at an exact frame.
        var slow = TimeSpan.FromMilliseconds(250);
        const int repeat = 6;

        using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(slow, repeat));

        // Read frame by frame until half of the run has arrived, then change only the frame.
        var before = 0;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (before < 3 && DateTime.UtcNow < deadline)
            before += rx.Receive(1, 50).Count(r => r.CanFrame.ID == 0x6A0);
        before.Should().Be(3);

        periodic.Update(frame: CanFrame.Classic(0x6A1, new byte[] { 0xBB }));

        // About three frames are left. Restarting the configured total would send six more,
        // which fits into this window as well.
        var after = 0;
        var until = DateTime.UtcNow.AddMilliseconds(2000);
        while (DateTime.UtcNow < until)
            after += rx.Receive(64, 50).Count(r => r.CanFrame.ID == 0x6A1);
        after.Should().BeInRange(1, repeat - 1);
    }

    [Fact]
    public void Immediate_Frame_That_Is_Not_Accepted_Does_Not_Reduce_The_Count()
    {
        using var tx = Open(0);
        using var rx = Open(1);

        // The driver rejects the immediate frame (full TX buffer); Transmit returns 0.
        // The failure is tied to an ID that only this test writes, so a transmit of a test
        // running in parallel cannot consume it.
        const int id = 0x6A3;
        CanKit.Adapter.Kvaser.Native.Canlib.FailNextWriteOf(id,
            CanKit.Adapter.Kvaser.Native.Canlib.canStatus.canERR_TXBUFOFL);
        var pending = true;
        try
        {
            using var periodic = tx.TransmitPeriodic(CanFrame.Classic(id, new byte[] { 0xAA }),
                new PeriodicTxOptions(Period, 3, true));

            // The immediate frame must have run into the injected failure. Otherwise the
            // count below would also be reached by the immediate frame plus two from the buffer.
            pending = CanKit.Adapter.Kvaser.Native.Canlib.ClearInjectedWriteFailure(id);
            pending.Should().BeFalse();

            Count(rx, TimeSpan.FromMilliseconds(400), id).Should().Be(3);
        }
        finally
        {
            if (pending) CanKit.Adapter.Kvaser.Native.Canlib.ClearInjectedWriteFailure(id);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void Unsupported_Message_Count_Uses_Software_Fallback_And_Can_Update(int initialRepeat)
    {
        const int id = 0x6A4;
        using var tx = Kvaser.Open(0, cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000)
            .SoftwareFeaturesFallBack(CanFeature.CyclicTx));
        using var rx = Open(1);
        Canlib.FailMsgCountFor(id, Canlib.canStatus.canERR_NOT_IMPLEMENTED);
        try
        {
            using var periodic = tx.TransmitPeriodic(CanFrame.Classic(id, new byte[] { 0xAA }),
                new PeriodicTxOptions(TimeSpan.FromMilliseconds(250), initialRepeat, false));
            periodic.Should().BeOfType<SoftwarePeriodicTx>();

            periodic.Update(repeatCount: 3);

            Count(rx, TimeSpan.FromSeconds(3), id).Should().Be(3);
        }
        finally
        {
            Canlib.ClearMsgCountFailure(id);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void Unsupported_Message_Count_Without_Fallback_Fails_Before_Sending(int initialRepeat)
    {
        const int id = 0x6A5;
        using var tx = Open(0);
        using var rx = Open(1);
        Canlib.FailMsgCountFor(id, Canlib.canStatus.canERR_NOT_IMPLEMENTED);
        try
        {
            var start = () => tx.TransmitPeriodic(CanFrame.Classic(id, new byte[] { 0xAA }),
                new PeriodicTxOptions(Period, initialRepeat));

            start.Should().Throw<CanKitException>();
            Count(rx, TimeSpan.FromMilliseconds(100), id).Should().Be(0);
        }
        finally
        {
            Canlib.ClearMsgCountFailure(id);
        }
    }

    private static ICanBus Open(int channel)
        => Kvaser.Open(channel, cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

    private static CanFrame Frame() => CanFrame.Classic(0x6A0, new byte[] { 0xAA });

    private static int Count(ICanBus bus, TimeSpan window, int id = 0x6A0)
    {
        var count = 0;
        var until = DateTime.UtcNow + window;
        while (DateTime.UtcNow < until)
            count += bus.Receive(64, 20).Count(r => r.CanFrame.ID == id);
        return count;
    }
}
#endif
