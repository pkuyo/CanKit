using System;
using System.Collections.Concurrent;
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
    public async Task Dispose_Called_Reentrantly_From_A_Posted_Callback_Does_Not_Deadlock()
    {
        // Regression test: a work item that disposes the very actor currently running it (e.g. a
        // protocol instance deciding to tear itself down) must not deadlock Thread.Join/Task.Wait
        // against itself.
        var actor = new ProtocolActor();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        actor.Post(() =>
        {
            actor.Dispose();
            completed.TrySetResult(true);
        });

        (await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(5)))).Should().Be(completed.Task,
            "a self-disposing callback must return promptly instead of deadlocking on its own loop");

        // The loop must still actually finish tearing itself down shortly afterward.
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Action postAfter = () => actor.Post(() => { });
        postAfter.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task PostAsync_Fails_Its_Task_Instead_Of_Hanging_When_SynchronizationContext_Send_Throws()
    {
        // Regression test: if Send itself throws before ever invoking the wrapped work, PostAsync's
        // own try/catch (inside that wrapped work) never runs -- nothing else may complete its
        // TaskCompletionSource, or the caller hangs forever despite FR-RAW-023's guarantee that
        // PostAsync failures surface via the returned task.
        var throwing = new AlwaysThrowingSynchronizationContext();
        using var actor = new ProtocolActor(ActorExecutionMode.SynchronizationContext, throwing);

        Func<Task> act = () => actor.PostAsync(() => { });

        await act.Should().ThrowAsync<InvalidOperationException>();
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
    public async Task Concurrent_Dispose_Never_Accepts_Work_That_Then_Fails_To_Run()
    {
        // Regression test: Post/Schedule must not be able to pass ThrowIfDisposed, lose a race to a
        // concurrent Dispose, and still enqueue work that FinalDrain has already run past (or never
        // runs at all). Every call that does *not* throw ObjectDisposedException must have its work
        // genuinely execute -- not hang forever.
        for (var iteration = 0; iteration < 25; iteration++)
        {
            var actor = new ProtocolActor();
            var accepted = new ConcurrentBag<Task>();

            var posters = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
            {
                for (var i = 0; i < 25; i++)
                {
                    try
                    {
                        accepted.Add(actor.PostAsync(() => 0));
                    }
                    catch (ObjectDisposedException)
                    {
                        // Acceptable: this call lost the race to a concurrent Dispose.
                    }
                }
            })).ToArray();

            foreach (var t in posters) t.Start();
            actor.Dispose();
            foreach (var t in posters) t.Join();

            foreach (var task in accepted)
            {
                (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)))).Should().Be(task,
                    "work accepted before Dispose observed it must still run to completion instead of hanging forever");
            }
        }
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
    public async Task Schedule_Still_Fires_Promptly_When_The_SynchronizationContext_Defers_Posted_Work()
    {
        // Regression test: Schedule's timer-list insertion must never be marshaled through the
        // SynchronizationContext -- only the eventual callback (user-facing work) should be. With
        // a context that genuinely defers execution (unlike RecordingSynchronizationContext above,
        // which runs inline), a deferred *insertion* would race the loop's own bookkeeping and
        // could leave the timer sitting unnoticed forever, since nothing re-signals the loop once
        // a deferred insert eventually lands.
        var deferred = new DeferredSynchronizationContext();
        using var actor = new ProtocolActor(ActorExecutionMode.SynchronizationContext, deferred);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handle = actor.Schedule(TimeSpan.FromMilliseconds(100), () => tcs.TrySetResult(true));

        // Wait until the actor loop has posted the due callback into the deferred context.
        // A fixed sleep-then-FlushOnce is flaky on slow CI (notably Windows/net48): if FlushOnce
        // runs before the loop posts, the callback lands later with nobody left to flush it.
        (await deferred.WaitForQueuedAsync(TimeSpan.FromSeconds(5))).Should().BeTrue(
            "Schedule must insert the timer independently of the SynchronizationContext so the due callback is posted once the delay elapses");

        // The callback itself *is* user-facing work, so it must still be waiting for a flush --
        // this alone proves the context is genuinely wired in, not bypassed entirely.
        tcs.Task.IsCompleted.Should().BeFalse();

        // Flushing must make it fire essentially immediately: the timer already became due and was
        // sitting in the actor's own timer list (inserted inline, independent of the context) --
        // if the insertion had instead been deferred through the context like the callback is,
        // WaitForQueuedAsync above would have timed out instead.
        deferred.FlushOnce();
        (await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)))).Should().Be(tcs.Task);
    }

    [Fact]
    public async Task Dispose_In_SynchronizationContext_Mode_Runs_Already_Queued_Work_To_Completion()
    {
        // Regression test: FinalDrain must rely on RunSafely's blocking SynchronizationContext.Send,
        // not a fire-and-forget Post, or Dispose could return (and this PostAsync could hang
        // forever) while the work is only sitting in this context's Post queue, never executed.
        // PostOnlyQueueingSynchronizationContext only overrides Post (queuing without running it) and
        // leaves Send at its default -- which invokes the callback inline/synchronously -- so this
        // would fail if RunSafely ever again marshaled through Post instead of Send.
        var context = new PostOnlyQueueingSynchronizationContext();
        var actor = new ProtocolActor(ActorExecutionMode.SynchronizationContext, context);
        var pending = actor.PostAsync(() => 7);

        actor.Dispose();

        (await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2)))).Should().Be(pending,
            "Dispose must run already-queued PostAsync work to completion even when marshaled through a SynchronizationContext");
        (await pending).Should().Be(7);
    }

    [Fact]
    public async Task SynchronizationContext_Send_Failure_Surfaces_Via_BackgroundExceptionOccurred_And_Loop_Keeps_Running()
    {
        // Regression test: RunSafely's try/catch previously only wrapped the user callback inside
        // the delegate passed to Send, not the Send call itself. A Send that throws directly (e.g.
        // the target context was torn down) must not escape the loop -- it needs the exact same
        // "caught and raised via BackgroundExceptionOccurred, loop keeps running" treatment as any
        // other failure source (FR-RAW-023).
        var throwing = new AlwaysThrowingSynchronizationContext();
        using var actor = new ProtocolActor(ActorExecutionMode.SynchronizationContext, throwing);
        var observed = new ConcurrentBag<Exception>();
        using var gate = new SemaphoreSlim(0);
        actor.BackgroundExceptionOccurred += (_, ex) => { observed.Add(ex); gate.Release(); };

        actor.Post(() => { });
        (await gate.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue("Send's own exception must be observable, not lost or crash the loop");
        observed.Should().ContainSingle().Which.Should().BeOfType<InvalidOperationException>();

        // The loop itself must still be alive and processing subsequent items afterward.
        actor.Post(() => { });
        (await gate.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue("one Send failure must never take down the whole actor");
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

        // ProtocolActor marshals via the blocking Send, not fire-and-forget Post -- see
        // RunSafely's comment for why (Dispose's "queued work completes" guarantee depends on it).
        public override void Send(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref PostCount);
            d(state);
        }
    }

    /// <summary>
    /// A <see cref="SynchronizationContext"/> that genuinely defers work instead of running it
    /// inline (deliberately violating <see cref="Send"/>'s normal blocking-until-done contract),
    /// so tests can prove something does or does not depend on the context actually being
    /// serviced.
    /// </summary>
    private sealed class DeferredSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly SemaphoreSlim _queued = new(0);

        public override void Send(SendOrPostCallback d, object? state)
        {
            _queue.Enqueue((d, state));
            _queued.Release();
        }

        public Task<bool> WaitForQueuedAsync(TimeSpan timeout) => _queued.WaitAsync(timeout);

        public void FlushOnce()
        {
            while (_queue.TryDequeue(out var item))
                item.Callback(item.State);
        }
    }

    /// <summary>
    /// A <see cref="SynchronizationContext"/> that only overrides <see cref="Post"/> (queuing
    /// without running it) and deliberately leaves <see cref="Send"/> at the base
    /// implementation's default synchronous-invoke behavior, so a test can distinguish "marshaled
    /// via blocking Send" from "marshaled via fire-and-forget Post".
    /// </summary>
    private sealed class PostOnlyQueueingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { /* intentionally never runs d */ }
    }

    /// <summary>
    /// A <see cref="SynchronizationContext"/> whose <see cref="Send"/> always throws, simulating a
    /// torn-down dispatcher, so tests can prove a failure in the marshal call itself (not just in
    /// the marshaled work) is caught rather than escaping the actor loop.
    /// </summary>
    private sealed class AlwaysThrowingSynchronizationContext : SynchronizationContext
    {
        public override void Send(SendOrPostCallback d, object? state) => throw new InvalidOperationException("context torn down");
    }
}
