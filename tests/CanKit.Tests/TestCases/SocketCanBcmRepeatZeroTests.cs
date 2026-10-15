using System;
using System.Linq;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// SocketCAN BCM periodic TX with a zero count sends nothing, as documented on
/// <see cref="PeriodicTxOptions.Repeat"/>. The kernel sends one frame whenever a job is set up
/// with <c>STARTTIMER</c>, whatever the count is, so a zero count must not start the timer.
///
/// Self-skips unless SocketCAN is the adapter under test, and shares the BCM-only interface
/// and the collection of <see cref="SocketCanBcmOwnershipTests"/>, so no other BCM job runs
/// on the interface at the same time. Runs against the kernel in Release and against the
/// in-memory backend in Fake.
/// </summary>
[Collection("SocketCanBcmOwnership")]
public class SocketCanBcmRepeatZeroTests
{
    private const string BcmOnlyEndpoint = "socketcan://vcan2";
    private const int Id = 0x6B0;

    private static bool ShouldRun()
    {
        var env = Environment.GetEnvironmentVariable("CANKIT_TEST_ADAPTERS");
        return string.Equals(env, "CanKit.Adapter.SocketCAN", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repeat_Zero_Sends_Nothing()
    {
        if (!ShouldRun()) return;

        using var rx = Open();
        using var tx = Open();

        using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(TimeSpan.FromMilliseconds(10), 0));

        Count(rx, TimeSpan.FromMilliseconds(300)).Should().Be(0);
    }

    [Fact]
    public void Update_To_Zero_Stops_Without_Sending_Another_Frame()
    {
        if (!ShouldRun()) return;

        using var rx = Open();
        using var tx = Open();

        // The period is long enough that the next regular frame is far away when the count
        // is set to zero; any frame seen afterwards was caused by the update itself.
        using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(TimeSpan.FromSeconds(2), -1));

        var first = 0;
        var deadline = DateTime.UtcNow.AddSeconds(1.5);
        while (first == 0 && DateTime.UtcNow < deadline)
            first += rx.Receive(1, 20).Count(r => r.CanFrame.ID == Id);
        first.Should().Be(1);

        periodic.Update(repeatCount: 0);

        Count(rx, TimeSpan.FromMilliseconds(2500)).Should().Be(0);
    }

    [Fact]
    public void Update_From_Zero_To_A_Count_Sends_That_Count()
    {
        if (!ShouldRun()) return;

        using var rx = Open();
        using var tx = Open();

        using var periodic = tx.TransmitPeriodic(Frame(), new PeriodicTxOptions(TimeSpan.FromMilliseconds(20), 0));
        periodic.Update(repeatCount: 3);

        Count(rx, TimeSpan.FromMilliseconds(600)).Should().Be(3);
    }

    private static ICanBus Open()
        => CanBus.Open(BcmOnlyEndpoint, cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20)
            .Baud(500_000)
            .SetAsyncBufferCapacity(64));

    private static CanFrame Frame() => CanFrame.Classic(Id, new byte[] { 0xAA });

    private static int Count(ICanBus bus, TimeSpan window)
    {
        var count = 0;
        var until = DateTime.UtcNow + window;
        while (DateTime.UtcNow < until)
            count += bus.Receive(64, 20).Count(r => r.CanFrame.ID == Id);
        return count;
    }
}
