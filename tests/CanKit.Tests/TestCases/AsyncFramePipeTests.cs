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

        var items = await read.WaitAsync(System.TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAll_Yields_Remaining_Items_Then_Ends_After_Complete()
    {
        var pipe = new AsyncFramePipe<int>();
        pipe.Publish(1);
        pipe.Publish(2);
        pipe.Complete();

        var items = await ReadAllAsync(pipe).WaitAsync(System.TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
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

        var batch = await read.WaitAsync(System.TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        batch.Should().Equal(7);
    }

    [Fact]
    public async Task ReceiveBatch_After_Complete_Returns_Empty_Instead_Of_Waiting()
    {
        var pipe = new AsyncFramePipe<int>();
        pipe.Complete();

        var batch = await pipe.ReceiveBatchAsync(1, -1, TestContext.Current.CancellationToken)
            .WaitAsync(System.TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
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
