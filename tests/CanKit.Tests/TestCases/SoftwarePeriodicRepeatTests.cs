using System;
using System.Linq;
using System.Threading;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// <see cref="CanKit.Core.Utils.SoftwarePeriodicTx"/> sends <c>Repeat</c> frames in total.
/// <c>Repeat = 0</c> sends nothing, including no <c>FireImmediately</c> frame.
/// Runs on the in-process virtual adapter, whose periodic TX is the software scheduler.
/// </summary>
public class SoftwarePeriodicRepeatTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(10);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Repeat_Zero_Sends_Nothing(bool fireImmediately)
    {
        var (tx, rx) = OpenPair();
        using (tx)
        using (rx)
        {
            using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, 0, fireImmediately));

            Count(rx, TimeSpan.FromMilliseconds(200)).Should().Be(0);
            periodic.RemainingCount.Should().Be(0);
        }
    }

    [Fact]
    public void Update_To_Zero_Repeats_Stops_Sending()
    {
        var (tx, rx) = OpenPair();
        using (tx)
        using (rx)
        {
            using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, -1));
            Count(rx, TimeSpan.FromMilliseconds(100)).Should().BeGreaterThan(0);

            periodic.Update(repeatCount: 0);
            Thread.Sleep(50); // let a send that was already in flight arrive

            Count(rx, TimeSpan.FromMilliseconds(50)).Should().BeLessThanOrEqualTo(1);
            Count(rx, TimeSpan.FromMilliseconds(200)).Should().Be(0);
        }
    }

    [Fact]
    public void Finite_Repeat_Sends_Exactly_Repeat_Frames()
    {
        var (tx, rx) = OpenPair();
        using (tx)
        using (rx)
        {
            using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, 3));

            Count(rx, TimeSpan.FromMilliseconds(300)).Should().Be(3);
        }
    }

    [Fact]
    public void Update_To_Zero_Stops_A_Waiting_Schedule_Without_Waiting_For_The_Next_Period()
    {
        var (tx, rx) = OpenPair();
        using (tx)
        using (rx)
        {
            // Long period: after the immediate frame the scheduler waits ten seconds.
            using var periodic = tx.TransmitPeriodic(Frame(),
                new PeriodicTxOptions(TimeSpan.FromSeconds(10), -1));
            var schedule = periodic.Should().BeOfType<CanKit.Core.Utils.SoftwarePeriodicTx>().Subject;
            Count(rx, TimeSpan.FromMilliseconds(100)).Should().Be(1);
            schedule.IsRunning.Should().BeTrue();

            periodic.Update(repeatCount: 0);

            schedule.IsRunning.Should().BeFalse("a zero count stops the schedule right away");
            periodic.RemainingCount.Should().Be(0);
            // Not only the flag: the worker itself must be gone, not asleep until the next period.
            schedule.WorkerTask!.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue("the worker must not sleep on");
        }
    }

    [Fact]
    public void Stop_Ends_The_Worker_And_Sends_No_Further_Frame()
    {
        var (tx, rx) = OpenPair();
        using (tx)
        using (rx)
        {
            using var periodic = tx.TransmitPeriodic(Frame(),
                new PeriodicTxOptions(TimeSpan.FromMilliseconds(300), -1));
            var schedule = periodic.Should().BeOfType<CanKit.Core.Utils.SoftwarePeriodicTx>().Subject;
            Count(rx, TimeSpan.FromMilliseconds(100)).Should().Be(1);

            periodic.Stop();

            schedule.WorkerTask!.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue("Stop() interrupts the wait");
            // The frame that was due 300 ms after the first one must not be sent any more.
            Count(rx, TimeSpan.FromMilliseconds(500)).Should().Be(0);
        }
    }

    private static (ICanBus tx, ICanBus rx) OpenPair()
    {
        var session = "swperiodic" + Guid.NewGuid().ToString("N");
        var tx = CanBus.Open($"virtual://{session}/0", cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20)
            .Baud(500_000)
            .SoftwareFeaturesFallBack(CanFeature.CyclicTx));
        var rx = CanBus.Open($"virtual://{session}/1", cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20)
            .Baud(500_000));
        return (tx, rx);
    }

    private static CanFrame Frame() => CanFrame.Classic(0x6B0, new byte[] { 0xBB });

    private static int Count(ICanBus bus, TimeSpan window)
    {
        var count = 0;
        var until = DateTime.UtcNow + window;
        while (DateTime.UtcNow < until)
            count += bus.Receive(64, 20).Count(r => r.CanFrame.ID == 0x6B0);
        return count;
    }
}
