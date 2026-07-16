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

    [Fact]
    public async Task ReceiveBatchAsync_User_Cancellation_Preferred_Over_Background_Fault()
    {
        var pipe = new AsyncFramePipe<int>();
        using var cts = new CancellationTokenSource();

        var waitingRead = pipe.ReceiveBatchAsync(1, Timeout.Infinite, cts.Token);
        await Task.Delay(20);

        // Both signals race the wait; caller cancellation must not be masked by the pulse.
        cts.Cancel();
        pipe.ExceptionOccured(new InvalidOperationException("background"));

        var act = async () => await waitingRead;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReceiveBatchAsync_Timeout_Returns_Partial_Batch()
    {
        var pipe = new AsyncFramePipe<int>();
        pipe.Publish(1);

        var batch = await pipe.ReceiveBatchAsync(2, 50, CancellationToken.None);

        batch.Should().Equal(1);
    }

    [Fact]
    public async Task ReadAllAsync_Background_Exception_Does_Not_Cause_Subsequent_Frame_Loss()
    {
        var pipe = new AsyncFramePipe<int>();
        var exception = new InvalidOperationException("boom");

        await using var enumerator = pipe.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        var waitingMove = enumerator.MoveNextAsync().AsTask();

        pipe.ExceptionOccured(exception);

        var act = async () => await waitingMove;
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");

        pipe.Publish(7);

        var batch = await pipe.ReceiveBatchAsync(1, 1_000, CancellationToken.None);
        batch.Should().Equal(7);
    }

    [Fact]
    public async Task ReadAllAsync_User_Cancellation_Propagates()
    {
        var pipe = new AsyncFramePipe<int>();
        using var cts = new CancellationTokenSource();

        await using var enumerator = pipe.ReadAllAsync(cts.Token).GetAsyncEnumerator();
        var waitingMove = enumerator.MoveNextAsync().AsTask();

        cts.Cancel();

        var act = async () => await waitingMove;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Exception_Pulse_During_Timeout_Wait_Surfaces_Fault()
    {
        var pipe = new AsyncFramePipe<int>();
        var exception = new InvalidOperationException("timed-fault");

        var waitingRead = pipe.ReceiveBatchAsync(1, 5_000, CancellationToken.None);
        await Task.Delay(20);
        pipe.ExceptionOccured(exception);

        var act = async () => await waitingRead;
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("timed-fault");
    }

    [Fact]
    public async Task Background_OperationCanceledException_Is_Not_Treated_As_Timeout()
    {
        var pipe = new AsyncFramePipe<int>();
        var exception = new OperationCanceledException("background-cancel");

        var waitingRead = pipe.ReceiveBatchAsync(1, Timeout.Infinite, CancellationToken.None);

        pipe.ExceptionOccured(exception);

        var act = async () => await waitingRead;
        await act.Should().ThrowAsync<OperationCanceledException>()
            .WithMessage("background-cancel");
    }

    [Fact]
    public async Task Background_TaskCanceledException_During_Timeout_Wait_Surfaces_Fault()
    {
        var pipe = new AsyncFramePipe<int>();
        var exception = new TaskCanceledException("timed-cancel-fault");

        var waitingRead = pipe.ReceiveBatchAsync(1, 5_000, CancellationToken.None);
        await Task.Delay(20);
        pipe.ExceptionOccured(exception);

        var act = async () => await waitingRead;
        await act.Should().ThrowAsync<TaskCanceledException>()
            .WithMessage("timed-cancel-fault");
    }
}
