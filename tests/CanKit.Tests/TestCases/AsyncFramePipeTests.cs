using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CanKit.Core.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Core-only tests for the RX pipe behind ReceiveAsync and GetFramesAsync. Completing the pipe
/// is what lets a pending read end when the bus is disposed.
/// </summary>
public class AsyncFramePipeTests
{
    [Fact]
    public async Task Pending_ReadAll_Ends_After_Complete()
    {
        var pipe = new AsyncFramePipe<int>();
        var read = ReadAllAsync(pipe);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        read.IsCompleted.Should().BeFalse();

        pipe.Complete();

        var items = await read.WithinTwoSeconds();
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAll_Yields_Remaining_Items_Then_Ends_After_Complete()
    {
        var pipe = new AsyncFramePipe<int>();
        pipe.Publish(1);
        pipe.Publish(2);
        pipe.Complete();

        var items = await ReadAllAsync(pipe).WithinTwoSeconds();
        items.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Pending_Infinite_ReceiveBatch_Returns_What_It_Has_After_Complete()
    {
        var pipe = new AsyncFramePipe<int>();
        var read = pipe.ReceiveBatchAsync(2, -1, TestContext.Current.CancellationToken);
        pipe.Publish(7);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        read.IsCompleted.Should().BeFalse();

        pipe.Complete();

        var batch = await read.WithinTwoSeconds();
        batch.Should().Equal(7);
    }

    [Fact]
    public async Task ReceiveBatch_After_Complete_Returns_Empty_Instead_Of_Waiting()
    {
        var pipe = new AsyncFramePipe<int>();
        pipe.Complete();

        var batch = await pipe.ReceiveBatchAsync(1, -1, TestContext.Current.CancellationToken)
            .WithinTwoSeconds();
        batch.Should().BeEmpty();
    }

    [Fact]
    public void Publish_After_Complete_Hands_The_Item_To_The_Drop_Callback()
    {
        var dropped = new List<int>();
        var pipe = new AsyncFramePipe<int>(capacity: 4, onDropped: dropped.Add);
        pipe.Complete();

        pipe.Publish(42);

        dropped.Should().Equal(42);
    }

    [Fact]
    public void Clear_Hands_Queued_Items_To_The_Drop_Callback()
    {
        var dropped = new List<int>();
        var pipe = new AsyncFramePipe<int>(onDropped: dropped.Add);
        pipe.Publish(1);
        pipe.Publish(2);

        pipe.Clear();

        dropped.Should().Equal(1, 2);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public async Task Clear_Drains_Every_Item_When_Drop_Callbacks_Throw(int? capacity, bool complete)
    {
        var dropped = new List<int>();
        var pipe = new AsyncFramePipe<int>(capacity, item =>
        {
            dropped.Add(item);
            if (item % 2 != 0)
                throw new InvalidOperationException("Buffer disposal failed.");
        });
        for (var item = 1; item <= 4; item++)
            pipe.Publish(item);
        if (complete)
            pipe.Complete();

        Action clear = pipe.Clear;
        clear.Should().NotThrow("cleanup must continue to shutdown cancellation after draining");

        dropped.Should().Equal(1, 2, 3, 4);
        (await pipe.ReceiveBatchAsync(0, 0, TestContext.Current.CancellationToken)).Should().BeEmpty();
        pipe.Clear();
        dropped.Should().HaveCount(4, "a failed callback must not leave an item queued for another release");
    }

    [Fact]
    public void Publish_After_Complete_Does_Not_Propagate_A_Drop_Callback_Failure()
    {
        var dropped = new List<int>();
        var pipe = new AsyncFramePipe<int>(onDropped: item =>
        {
            dropped.Add(item);
            throw new InvalidOperationException("Buffer disposal failed.");
        });
        pipe.Complete();

        Action publish = () => pipe.Publish(42);
        publish.Should().NotThrow();
        dropped.Should().Equal(42);
    }

    [Fact]
    public async Task Bounded_Publish_Preserves_The_New_Item_When_The_Drop_Callback_Throws()
    {
        var dropped = new List<int>();
        var pipe = new AsyncFramePipe<int>(1, item =>
        {
            dropped.Add(item);
            throw new InvalidOperationException("Buffer disposal failed.");
        });
        pipe.Publish(1);

        Action publish = () => pipe.Publish(2);
        publish.Should().NotThrow();

        dropped.Should().Equal(1);
        (await pipe.ReceiveBatchAsync(0, 0, TestContext.Current.CancellationToken)).Should().Equal(2);
    }

    [Fact]
    public void Complete_Is_Idempotent()
    {
        var pipe = new AsyncFramePipe<int>();
        pipe.Complete();
        var again = () => pipe.Complete();
        again.Should().NotThrow();
    }

    private static async Task<List<T>> ReadAllAsync<T>(AsyncFramePipe<T> pipe)
    {
        var list = new List<T>();
        await foreach (var item in pipe.ReadAllAsync(TestContext.Current.CancellationToken))
            list.Add(item);
        return list;
    }
}

internal static class TaskTimeoutExtensions
{
    /// <summary>Task.WaitAsync is not available on .NET Framework, so race against a delay.</summary>
    public static async Task<T> WithinTwoSeconds<T>(this Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(System.TimeSpan.FromSeconds(2)));
        completed.Should().BeSameAs(task, "the task must complete within two seconds");
        return await task;
    }
}
