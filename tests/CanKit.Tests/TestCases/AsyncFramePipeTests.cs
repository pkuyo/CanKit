using System;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class AsyncFramePipeTests
{
    [Fact]
    public async Task Background_Exception_Does_Not_Cause_Subsequent_Frame_Loss()
    {
        var pipe = new AsyncFramePipe<int>();
        var exception = new InvalidOperationException("boom");

        var waitingRead = pipe.ReceiveBatchAsync(1, Timeout.Infinite, CancellationToken.None);

        pipe.ExceptionOccured(exception);

        var act = async () => await waitingRead;
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");

        pipe.Publish(42);

        var batch = await pipe.ReceiveBatchAsync(1, 1_000, CancellationToken.None);

        batch.Should().Equal(42);
    }

    [Fact]
    public async Task ReceiveBatchAsync_User_Cancellation_Propagates()
    {
        var pipe = new AsyncFramePipe<int>();
        using var cts = new CancellationTokenSource();

        var waitingRead = pipe.ReceiveBatchAsync(1, Timeout.Infinite, cts.Token);

        cts.Cancel();

        var act = async () => await waitingRead;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
