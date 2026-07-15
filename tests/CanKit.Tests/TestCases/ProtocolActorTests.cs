using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Pro.Actor;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Verifies the L2 protocol-instance actor/scheduler abstraction (CanKit.Pro.Actor, arc42
/// §8.3 / ADR-6, SRS FR-RAW-020..024): single-mailbox single-writer execution, event-driven
/// timer scheduling, the background-exception channel, and configurable execution context.
/// </summary>
public class ProtocolActorTests
{
    [Fact]
    public async Task Post_Executes_Work_Sequentially_In_Posting_Order()
    {
        using var actor = new ProtocolActor();
        var order = new List<int>();

        for (var i = 0; i < 50; i++)
        {
            var captured = i;
            actor.Post(() => order.Add(captured));
        }

        // Drain via a request/response call, which only completes once every prior fire-and-forget
        // Post has already run (single mailbox, strict FIFO).
        await actor.PostAsync(() => 0);

        order.Should().Equal(Enumerable.Range(0, 50));
    }

    [Fact]
    public async Task DedicatedThread_Mode_Runs_Every_Callback_On_The_Same_Thread()
    {
        using var actor = new ProtocolActor(ActorExecutionMode.DedicatedThread);
        var threadIds = new List<int>();

        for (var i = 0; i < 20; i++)
            threadIds.Add(await actor.PostAsync(() => Thread.CurrentThread.ManagedThreadId));

        threadIds.Distinct().Should().ContainSingle("every callback must run on the actor's one dedicated thread (FR-RAW-024)");
        threadIds[0].Should().NotBe(Thread.CurrentThread.ManagedThreadId);
    }

    [Fact]
    public async Task ThreadPool_Mode_Still_Serializes_Work_With_No_Lost_Updates()
    {
        // Single-writer safety (FR-RAW-021) must hold even without thread affinity: many
        // concurrent PostAsync callers incrementing shared, unsynchronized state must never lose
        // an update, because the actor itself guarantees only one work item runs at a time.
        using var actor = new ProtocolActor(ActorExecutionMode.ThreadPool);
        var counter = 0;
        const int n = 500;

        var tasks = Enumerable.Range(0, n).Select(_ => actor.PostAsync(() => counter++)).ToArray();
        await Task.WhenAll(tasks);

        counter.Should().Be(n);
    }

    [Fact]
    public async Task Concurrent_Callers_From_Real_Threads_Produce_Exact_Count_No_Data_Races()
    {
        // FR-RAW-020/021 stress verification: parallel Post calls from genuine OS threads mutating
        // unsynchronized actor-owned state must still land exactly once each.
        using var actor = new ProtocolActor();
        var counter = 0;
        const int n = 500;

        var tasks = Enumerable.Range(0, n).Select(_ => Task.Run(() => actor.PostAsync(() => counter++))).ToArray();
        await Task.WhenAll(tasks);

        counter.Should().Be(n);
    }

    [Fact]
    public async Task PostAsync_Propagates_Exception_Through_The_Returned_Task_Without_Raising_BackgroundEvent()
    {
        using var actor = new ProtocolActor();
        var backgroundEventRaised = false;
        actor.BackgroundExceptionOccurred += (_, _) => backgroundEventRaised = true;

        Func<Task> act = () => actor.PostAsync<int>(() => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        // Give the loop a moment to have raised the event if it were (incorrectly) going to.
        await actor.PostAsync(() => 0);
        backgroundEventRaised.Should().BeFalse("PostAsync callers observe failures via the returned task, not the background-exception channel");
    }

    [Fact]
    public async Task Post_Exception_Surfaces_Via_BackgroundExceptionOccurred_And_The_Loop_Keeps_Running()
    {
        using var actor = new ProtocolActor();
        Exception? observed = null;
        using var gate = new SemaphoreSlim(0);
        actor.BackgroundExceptionOccurred += (_, ex) => { observed = ex; gate.Release(); };

        actor.Post(() => throw new InvalidOperationException("background boom"));

        (await gate.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue("the exception must be observable within a bounded time, not lost");
        observed.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("background boom");

        // FR-RAW-023: one failing item must never take down the whole actor.
        var stillWorks = await actor.PostAsync(() => 42);
        stillWorks.Should().Be(42);
    }

    [Fact]
    public async Task Schedule_Fires_Callback_After_The_Configured_Delay()
    {
        using var actor = new ProtocolActor();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sw = Stopwatch.StartNew();

        using var handle = actor.Schedule(TimeSpan.FromMilliseconds(200), () => tcs.TrySetResult(true));

        (await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)))).Should().Be(tcs.Task);
        sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task Disposing_The_Schedule_Handle_Before_Due_Prevents_The_Callback_From_Firing()
    {
        using var actor = new ProtocolActor();
        var fired = false;

        var handle = actor.Schedule(TimeSpan.FromMilliseconds(100), () => fired = true);
        handle.Dispose();

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        // Round-trip through the actor once more so we know the loop has definitely passed the
        // point where the (cancelled) timer would have fired.
        await actor.PostAsync(() => 0);

        fired.Should().BeFalse();
    }

    [Fact]
    public async Task Post_Wakes_The_Loop_Promptly_Even_While_A_Long_Timer_Is_Pending()
    {
        // Demonstrates event-driven waiting rather than a busy/poll loop (FR-RAW-022): a Post
        // issued while the loop is blocked waiting for a much later timer deadline must still be
        // processed promptly, not only once that unrelated timer eventually elapses.
        using var actor = new ProtocolActor();
        using var longTimer = actor.Schedule(TimeSpan.FromSeconds(30), () => { });

        var sw = Stopwatch.StartNew();
        var result = await actor.PostAsync(() => 1 + 1);
        sw.Stop();

        result.Should().Be(2);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Dispose_Runs_Already_Queued_Work_To_Completion_Instead_Of_Hanging()
    {
        var actor = new ProtocolActor();
        var pending = actor.PostAsync(() => 7);

        actor.Dispose();

        var result = await pending;
        result.Should().Be(7);
    }

    [Fact]
    public async Task Dispose_Rejects_New_Work_With_ObjectDisposedException()
    {
        var actor = new ProtocolActor();
        actor.Dispose();

        Action post = () => actor.Post(() => { });
        Func<Task> postAsync = () => actor.PostAsync(() => 0);
        Action schedule = () => actor.Schedule(TimeSpan.FromMilliseconds(10), () => { });

        post.Should().Throw<ObjectDisposedException>();
        await postAsync.Should().ThrowAsync<ObjectDisposedException>();
        schedule.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task SynchronizationContext_Mode_Marshals_Work_Through_The_Provided_Context()
    {
        var recording = new RecordingSynchronizationContext();
        using var actor = new ProtocolActor(ActorExecutionMode.SynchronizationContext, recording);

        var result = await actor.PostAsync(() => 99);

        result.Should().Be(99);
        recording.PostCount.Should().BeGreaterThan(0, "every callback must be marshaled through the supplied SynchronizationContext");
    }

    [Fact]
    public void Constructor_Requires_A_Context_For_SynchronizationContext_Mode()
    {
        Action act = () => new ProtocolActor(ActorExecutionMode.SynchronizationContext, synchronizationContext: null);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_Rejects_A_Context_For_Non_SynchronizationContext_Modes()
    {
        Action act = () => new ProtocolActor(ActorExecutionMode.ThreadPool, new RecordingSynchronizationContext());
        act.Should().Throw<ArgumentException>();
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public int PostCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref PostCount);
            d(state);
        }
    }
}
