using System;
using System.Linq;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Core.Utils;
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
    public async Task Update_To_Zero_Repeats_Stops_Sending()
    {
        var (tx, rx) = OpenPair();
        using (tx)
        using (rx)
        {
            using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, -1));
            await WaitForFramesAsync(rx, 1);

            periodic.Update(repeatCount: 0);
            await WaitForWorkerToEndAsync(periodic);
            // Update waits for the worker to stop. Discard frames already received before
            // it returned; a busy sender can leave more than one queued at this point.
            _ = rx.Receive(0, 0).ToArray();
            Count(rx, TimeSpan.FromMilliseconds(200)).Should().Be(0);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_From_Zero_To_A_Count_Sends_That_Count(bool fireImmediately)
    {
        var (tx, rx) = OpenPair();
        using (tx)
        using (rx)
        {
            // Created with a zero count, the schedule has no worker yet.
            using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, 0, fireImmediately));
            periodic.Update(repeatCount: 0); // still nothing to send, and not a stop
            Count(rx, TimeSpan.FromMilliseconds(50)).Should().Be(0);

            periodic.Update(repeatCount: 3);

            await WaitForFramesAsync(rx, 3);
            await WaitForWorkerToEndAsync(periodic);
            rx.Receive(0, 0).Should().BeEmpty("the finite schedule must send exactly three frames");
            periodic.RemainingCount.Should().Be(0);
        }
    }

    [Fact]
    public void Update_After_Stop_Does_Not_Start_A_Schedule_Created_With_Zero()
    {
        var (tx, rx) = OpenPair();
        using (tx)
        using (rx)
        {
            using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, 0));
            periodic.Stop();

            periodic.Update(repeatCount: 3);

            Count(rx, TimeSpan.FromMilliseconds(200)).Should().Be(0);
        }
    }

    [Fact]
    public async Task Finite_Repeat_Sends_Exactly_Repeat_Frames()
    {
        var (tx, rx) = OpenPair();
        using (tx)
        using (rx)
        {
            using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(Period, 3));

            await WaitForFramesAsync(rx, 3);
            await WaitForWorkerToEndAsync(periodic);
            rx.Receive(0, 0).Should().BeEmpty("the finite schedule must send exactly three frames");
        }
    }

    [Fact]
    public async Task Update_To_Zero_Stops_A_Waiting_Schedule_Without_Waiting_For_The_Next_Period()
    {
        var (tx, rx) = OpenPair();
        using (tx)
        using (rx)
        {
            // Long period: after the immediate frame the scheduler waits ten seconds.
            using var periodic = tx.TransmitPeriodic(Frame(),
                new PeriodicTxOptions(TimeSpan.FromSeconds(10), -1));
            var schedule = periodic.Should().BeOfType<SoftwarePeriodicTx>().Subject;
            await WaitForFramesAsync(rx, 1);
            schedule.IsRunning.Should().BeTrue();

            periodic.Update(repeatCount: 0);

            schedule.IsRunning.Should().BeFalse("a zero count stops the schedule right away");
            periodic.RemainingCount.Should().Be(0);
            // Not only the flag: the worker itself must be gone, not asleep until the next period.
            await WaitForWorkerToEndAsync(periodic);
        }
    }

    [Fact]
    public async Task Stop_Ends_The_Worker_And_Sends_No_Further_Frame()
    {
        var (tx, rx) = OpenPair();
        using (tx)
        using (rx)
        {
            using var periodic = tx.TransmitPeriodic(Frame(),
                new PeriodicTxOptions(TimeSpan.FromMilliseconds(300), -1));
            await WaitForFramesAsync(rx, 1);

            periodic.Stop();

            await WaitForWorkerToEndAsync(periodic);
            // Exclude frames already queued before Stop() returned on a busy runner.
            _ = rx.Receive(0, 0).ToArray();
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

    private static async Task WaitForFramesAsync(ICanBus bus, int count)
    {
        // Worker startup on a busy CI runner is not guaranteed within a 100/300 ms window.
        var frames = await bus.ReceiveAsync(count, 5000, TestContext.Current.CancellationToken);
        frames.Should().HaveCount(count, "the worker must send the expected frames within the startup timeout");
        frames.Should().OnlyContain(frame => frame.CanFrame.ID == 0x6B0);
    }

    private static async Task WaitForWorkerToEndAsync(IPeriodicTx periodic)
    {
        var worker = periodic.Should().BeOfType<SoftwarePeriodicTx>().Subject.WorkerTask;
        worker.Should().NotBeNull();
        var finished = await Task.WhenAny(worker!, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        finished.Should().BeSameAs(worker, "the worker must end without waiting for the next period");
        await worker!;
    }

    private static int Count(ICanBus bus, TimeSpan window)
    {
        var count = 0;
        var until = DateTime.UtcNow + window;
        while (DateTime.UtcNow < until)
            count += bus.Receive(64, 20).Count(r => r.CanFrame.ID == 0x6B0);
        return count;
    }
}
