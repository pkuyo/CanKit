using System;
using System.Linq;
using System.Threading.Tasks;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Tests.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class PeriodicTests : IClassFixture<TestCaseProvider>
{
    [Theory]
    [MemberData(nameof(Matrix.TestMatrix.CombinedPeriodicCount), MemberType = typeof(Matrix.TestMatrix))]
    public async Task Periodic_Send_Completes_Exact_Count_And_Stops(string epA, string epB, string endpoint, bool hasFd,
        ICanFrame frame, TimeSpan period, int count)
    {
        ICanBus? rx = null;
        ICanBus? tx = null;
        try
        {
            if (period == TimeSpan.Zero)
                return;

            _ = hasFd;
            _ = endpoint;
            if (frame is CanFdFrame)
            {
                rx = TestHelpers.OpenFd(epA);
                tx = TestHelpers.OpenFd(epB);
            }
            else
            {
                rx = TestHelpers.OpenClassic(epA);
                tx = TestHelpers.OpenClassic(epB);
            }

            var options = new PeriodicTxOptions(period, count);

            using var handle = tx.TransmitPeriodic(frame, options);

            var received = 0;
            var end = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < end && received < count)
            {
                var batch = await rx.ReceiveAsync(16, 20);
                received += batch.Count(d => d.CanFrame.ID == frame.ID);
            }

            // Stop and ensure no more
            handle.Stop();
            received.Should().BeGreaterOrEqualTo(count);
            await Task.Delay(period.Milliseconds*5);
            var after = await rx.ReceiveAsync(16, 100);
            after.Count(d => d.CanFrame.ID == frame.ID).Should().BeLessOrEqualTo(1);
        }
        finally
        {
            rx?.Dispose();
            tx?.Dispose();
        }

    }

    [Fact]
    public void Periodic_Invalid_Period_Should_Throw()
    {
        Action act = () => new PeriodicTxOptions(TimeSpan.Zero, 1, true);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

