using System;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Pro.Actor;
using CanKit.Pro.Reliability;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Verifies the L2 deadline/timeout primitive (CanKit.Pro.Reliability, arc42 §5.3 / ADR-11,
/// SRS FR-RAW-050) built on top of CanKit.Pro.Actor: an armed deadline's expiry is actually
/// scheduled and checked on the actor's loop (regression against Review §1.1 Punkt 10
/// "Deadlines werden gepflegt, aber nie geprüft"), with a single race-free Pending → terminal
/// resolution.
/// </summary>
public class DeadlineTests
{
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Deadline_Fires_OnExpired_After_The_Timeout_Elapses()
    {
        using var actor = new ProtocolActor();
        var scheduler = new DeadlineScheduler(actor);
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var deadline = scheduler.Arm(TimeSpan.FromMilliseconds(30), () => fired.TrySetResult(true));

        (await Task.WhenAny(fired.Task, Task.Delay(Bounded))).Should().Be(fired.Task,
            "an armed deadline must actually be scheduled and fire, not sit as never-checked data");
        deadline.IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task Complete_Before_Expiry_Prevents_OnExpired_And_Is_Idempotent()
    {
        using var actor = new ProtocolActor();
        var scheduler = new DeadlineScheduler(actor);
        var fired = false;

        var deadline = scheduler.Arm(TimeSpan.FromMilliseconds(200), () => fired = true);

        deadline.Complete().Should().BeTrue("Complete wins the race well before the deadline would expire");
        deadline.IsCompleted.Should().BeTrue();

        // A second Complete and a following Dispose are idempotent no-ops that must not change the
        // already-decided terminal outcome.
        deadline.Complete().Should().BeFalse("a second Complete cannot win an already-resolved deadline");
        deadline.Dispose();
        deadline.IsCompleted.Should().BeTrue();
        deadline.IsCancelled.Should().BeFalse();

        // Let the original timer's due point pass and round-trip through the loop; onExpired must
        // never fire for a completed deadline.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await actor.PostAsync(() => 0);
        fired.Should().BeFalse();
    }

    [Fact]
    public async Task Cancel_Before_Expiry_Prevents_OnExpired()
    {
        using var actor = new ProtocolActor();
        var scheduler = new DeadlineScheduler(actor);
        var fired = false;

        var deadline = scheduler.Arm(TimeSpan.FromMilliseconds(200), () => fired = true);
        deadline.Dispose(); // Dispose == Cancel
        deadline.IsCancelled.Should().BeTrue();

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await actor.PostAsync(() => 0);
        fired.Should().BeFalse();
    }

    [Fact]
    public async Task Rearm_Before_Original_Expiry_Extends_The_Deadline()
    {
        using var actor = new ProtocolActor();
        var scheduler = new DeadlineScheduler(actor);
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var deadline = scheduler.Arm(TimeSpan.FromMilliseconds(120), () => fired.TrySetResult(true));

        // Re-arm to a much longer window well before the original 120 ms would elapse.
        await Task.Delay(TimeSpan.FromMilliseconds(40));
        deadline.Rearm(TimeSpan.FromMilliseconds(400)).Should().BeTrue("re-arming a still-pending deadline succeeds");

        // The ORIGINAL timer (120 ms from arm) must NOT fire -- Rearm superseded it, and the stale
        // pre-Rearm timer must be generation-guarded out rather than double-firing.
        (await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromMilliseconds(220)))).Should().NotBe(fired.Task,
            "the original timeout must have been superseded by Rearm");
        deadline.IsExpired.Should().BeFalse();

        // ...but the re-armed timer (400 ms from Rearm) eventually does fire.
        (await Task.WhenAny(fired.Task, Task.Delay(Bounded))).Should().Be(fired.Task,
            "the re-armed timeout must still fire at its new deadline");
        deadline.IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task Rearm_After_The_Deadline_Already_Resolved_Returns_False()
    {
        using var actor = new ProtocolActor();
        var scheduler = new DeadlineScheduler(actor);

        var completed = scheduler.Arm(TimeSpan.FromMilliseconds(500), () => { });
        completed.Complete().Should().BeTrue();
        completed.Rearm(TimeSpan.FromMilliseconds(500)).Should().BeFalse("a completed deadline cannot be re-armed");

        var cancelled = scheduler.Arm(TimeSpan.FromMilliseconds(500), () => { });
        cancelled.Dispose();
        cancelled.Rearm(TimeSpan.FromMilliseconds(500)).Should().BeFalse("a cancelled deadline cannot be re-armed");

        var expiredFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var expired = scheduler.Arm(TimeSpan.FromMilliseconds(30), () => expiredFired.TrySetResult(true));
        (await Task.WhenAny(expiredFired.Task, Task.Delay(Bounded))).Should().Be(expiredFired.Task);
        expired.Rearm(TimeSpan.FromMilliseconds(100)).Should().BeFalse("an expired deadline cannot be re-armed");
    }

    [Fact]
    public async Task Exception_From_OnExpired_Surfaces_Via_The_Actor_Background_Exception_Channel()
    {
        using var actor = new ProtocolActor();
        var scheduler = new DeadlineScheduler(actor);
        Exception? observed = null;
        using var gate = new SemaphoreSlim(0);
        actor.BackgroundExceptionOccurred += (_, ex) => { observed = ex; gate.Release(); };

        using var deadline = scheduler.Arm(
            TimeSpan.FromMilliseconds(30),
            () => throw new InvalidOperationException("deadline boom"));

        (await gate.WaitAsync(Bounded)).Should().BeTrue(
            "onExpired throwing must surface via the actor's BackgroundExceptionOccurred (FR-RAW-023), not as an unobserved exception");
        observed.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("deadline boom");
    }

    [Fact]
    public async Task Disposing_The_Owning_Actor_While_Pending_Never_Fires_The_Deadline_And_Escapes_No_Exception()
    {
        var actor = new ProtocolActor();
        var scheduler = new DeadlineScheduler(actor);
        var backgroundFaulted = false;
        actor.BackgroundExceptionOccurred += (_, _) => backgroundFaulted = true;
        var fired = false;

        using var deadline = scheduler.Arm(TimeSpan.FromMilliseconds(200), () => fired = true);

        // The actor's FinalDrain discards not-yet-due Schedule callbacks, so a deadline that was
        // still Pending simply never resolves (documented best-effort behavior).
        actor.Dispose();

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        fired.Should().BeFalse("a pending deadline whose actor is disposed must never fire");
        deadline.IsExpired.Should().BeFalse();
        backgroundFaulted.Should().BeFalse("disposing the actor under a pending deadline must not raise any exception");
    }

    [Fact]
    public void Arm_Validates_Its_Arguments()
    {
        using var actor = new ProtocolActor();
        var scheduler = new DeadlineScheduler(actor);

        Action nullCallback = () => scheduler.Arm(TimeSpan.FromMilliseconds(10), null!);
        Action negativeTimeout = () => scheduler.Arm(TimeSpan.FromMilliseconds(-1), () => { });

        nullCallback.Should().Throw<ArgumentNullException>();
        negativeTimeout.Should().Throw<ArgumentOutOfRangeException>();
    }
}
