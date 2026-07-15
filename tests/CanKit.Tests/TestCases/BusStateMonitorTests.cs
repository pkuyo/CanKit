using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Virtual;
using CanKit.Core;
using CanKit.Core.Definitions;
using CanKit.Pro.Actor;
using CanKit.Pro.Reliability;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Verifies the L2 bus-state monitor (CanKit.Pro.Reliability, arc42 §5.3 / ADR-11,
/// SRS FR-RAW-051) against the Virtual adapter: a self-rearming poll driven through the actor's
/// loop pushes edge-triggered BusState transitions, plus the pure IsTransmitBlocked/IsDegraded
/// helpers.
/// </summary>
public class BusStateMonitorTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(5);

    private static string NewSession() => $"busmonitor-{Guid.NewGuid():N}";

    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    // The Virtual adapter has no public API on ICanBus to drive BusState, but its hub exposes a
    // public SetBusState; reach the session's hub via the same reflection VirtualBusOwnershipTests
    // uses. This lets us simulate a real controller-state transition (e.g. entering BusOff) that
    // the monitor's poll should then observe.
    private static void DriveBusState(string session, BusState state)
    {
        var field = typeof(VirtualBusHub).GetField("_hubs", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VirtualBusHub._hubs field not found.");
        var hubs = (IDictionary)field.GetValue(null)!;
        var hub = hubs[session] as VirtualBusHub
            ?? throw new InvalidOperationException($"No VirtualBusHub for session '{session}'.");
        hub.SetBusState(state);
    }

    [Fact]
    public void CurrentState_Reflects_The_Bus_State_At_Construction()
    {
        var session = NewSession();
        using var bus = Open(session, 0);
        using var actor = new ProtocolActor();

        // Establish a known, non-default state before the monitor is even constructed.
        DriveBusState(session, BusState.ErrPassive);

        using var monitor = new BusStateMonitor(bus, actor);

        monitor.CurrentState.Should().Be(BusState.ErrPassive,
            "the monitor baselines CurrentState synchronously from the bus in its constructor");
    }

    [Fact]
    public async Task StateChanged_Is_Not_Raised_While_The_State_Is_Unchanged()
    {
        var session = NewSession();
        using var bus = Open(session, 0);
        using var actor = new ProtocolActor();
        using var monitor = new BusStateMonitor(bus, actor, TimeSpan.FromMilliseconds(20));

        var changes = 0;
        monitor.StateChanged += (_, _) => Interlocked.Increment(ref changes);

        // Let many poll ticks run without ever changing the bus state.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Volatile.Read(ref changes).Should().Be(0, "an unchanged state must never raise an edge-triggered event");
    }

    [Fact]
    public async Task StateChanged_Fires_On_A_Transition_Observed_By_The_Poll()
    {
        var session = NewSession();
        using var bus = Open(session, 0);
        using var actor = new ProtocolActor();
        using var monitor = new BusStateMonitor(bus, actor, TimeSpan.FromMilliseconds(20));

        var busOff = new TaskCompletionSource<BusStateChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.StateChanged += (_, e) => { if (e.Current == BusState.BusOff) busOff.TrySetResult(e); };

        DriveBusState(session, BusState.BusOff);

        (await Task.WhenAny(busOff.Task, Task.Delay(Bounded))).Should().Be(busOff.Task,
            "the self-rearming poll must observe the BusOff transition");
        var args = await busOff.Task;
        args.Previous.Should().Be(BusState.None);
        args.Current.Should().Be(BusState.BusOff);
        args.Current.IsTransmitBlocked().Should().BeTrue();
        monitor.CurrentState.Should().Be(BusState.BusOff);
    }

    [Fact]
    public async Task StateChanged_Fires_On_Recovery_Back_Down_From_BusOff()
    {
        var session = NewSession();
        using var bus = Open(session, 0);
        using var actor = new ProtocolActor();

        DriveBusState(session, BusState.BusOff);
        using var monitor = new BusStateMonitor(bus, actor, TimeSpan.FromMilliseconds(20));

        var recovered = new TaskCompletionSource<BusStateChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.StateChanged += (_, e) => { if (e.Current == BusState.ErrActive) recovered.TrySetResult(e); };

        // Recovery (BusOff -> ErrActive) matters too: protocols need to know when to resume.
        DriveBusState(session, BusState.ErrActive);

        (await Task.WhenAny(recovered.Task, Task.Delay(Bounded))).Should().Be(recovered.Task,
            "recovering transitions must be reported, not only degrading ones");
        var args = await recovered.Task;
        args.Previous.Should().Be(BusState.BusOff);
        args.Current.Should().Be(BusState.ErrActive);
    }

    [Fact]
    public async Task Dispose_Stops_Further_StateChanged_Events_And_Is_Idempotent()
    {
        var session = NewSession();
        using var bus = Open(session, 0);
        using var actor = new ProtocolActor();
        var monitor = new BusStateMonitor(bus, actor, TimeSpan.FromMilliseconds(20));

        var changes = 0;
        monitor.StateChanged += (_, _) => Interlocked.Increment(ref changes);

        monitor.Dispose();
        monitor.Dispose(); // idempotent

        // Change the state only after disposing: with the poll stopped, no event may arrive.
        DriveBusState(session, BusState.BusOff);
        await Task.Delay(TimeSpan.FromMilliseconds(150)); // several poll intervals

        Volatile.Read(ref changes).Should().Be(0, "a disposed monitor must stop polling and raising events");
    }

    [Theory]
    [InlineData(BusState.None, false)]
    [InlineData(BusState.ErrActive, false)]
    [InlineData(BusState.ErrWarning, false)]
    [InlineData(BusState.ErrPassive, false)]
    [InlineData(BusState.BusOff, true)]
    [InlineData(BusState.Unknown, false)]
    public void IsTransmitBlocked_Is_True_Only_For_BusOff(BusState state, bool expected)
        => state.IsTransmitBlocked().Should().Be(expected);

    [Theory]
    [InlineData(BusState.None, false)]
    [InlineData(BusState.ErrActive, false)]
    [InlineData(BusState.ErrWarning, true)]
    [InlineData(BusState.ErrPassive, true)]
    [InlineData(BusState.BusOff, true)]
    [InlineData(BusState.Unknown, false)]
    public void IsDegraded_Is_True_For_Warning_Passive_And_BusOff(BusState state, bool expected)
        => state.IsDegraded().Should().Be(expected);

    [Fact]
    public void Constructor_Rejects_A_Nonpositive_Poll_Interval()
    {
        var session = NewSession();
        using var bus = Open(session, 0);
        using var actor = new ProtocolActor();

        Action zero = () => new BusStateMonitor(bus, actor, TimeSpan.Zero);
        Action negative = () => new BusStateMonitor(bus, actor, TimeSpan.FromMilliseconds(-1));

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }
}
