using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests;

public class PeriodicTests : IClassFixture<TestAssemblyLoader>
{
    [Theory]
    [MemberData(nameof(TestMatrix.Pairs), MemberType = typeof(TestMatrix))]
    public async Task Periodic_Send_Completes_Exact_Count_And_Stops(string epA, string epB, string endpoint, bool hasFd)
    {
        _ = hasFd;
        _ = endpoint;
        using var rx = TestHelpers.OpenClassic(epA);
        using var tx = TestHelpers.OpenClassic(epB);

        var frame = new CanClassicFrame(0x501, new byte[] { 0x5, 0x1 });
        var options = new PeriodicTxOptions(TimeSpan.FromMilliseconds(5), 10);

        using var handle = tx.TransmitPeriodic(frame, options);

        var received = 0;
        var end = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < end && received < 10)
        {
            var batch = await rx.ReceiveAsync(16, 20);
            received += batch.Count(d => d.CanFrame.ID == 0x501);
        }

        received.Should().BeGreaterOrEqualTo(10);

        // Stop and ensure no more
        handle.Stop();
        await Task.Delay(50);
        var after = await rx.ReceiveAsync(16, 100);
        after.Count(d => d.CanFrame.ID == 0x501).Should().Be(0);
    }

    [Fact]
    public void Periodic_Invalid_Period_Should_Throw()
    {
        Action act = () => new PeriodicTxOptions(TimeSpan.Zero, 1, true);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

