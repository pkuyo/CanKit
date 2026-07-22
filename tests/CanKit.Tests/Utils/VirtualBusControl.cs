using System;
using System.Collections;
using System.Reflection;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Virtual;
using CanKit.Core.Diagnostics;

namespace CanKit.Tests.Utils;

/// <summary>
/// Test-only control hooks for the Virtual adapter, reached via reflection because the adapter
/// deliberately exposes no public API for them on <see cref="ICanBus"/>: driving the session
/// hub's <see cref="BusState"/> (e.g. into BusOff) and reporting a fault through the bus's
/// <see cref="CanBusExceptionDispatcher"/> (the same path a real adapter's error handler uses).
/// Same technique as <c>BusStateMonitorTests.DriveBusState</c>.
/// </summary>
internal static class VirtualBusControl
{
    /// <summary>Drives the shared hub of <paramref name="session"/> to <paramref name="state"/>,
    /// simulating a controller-state transition that every bus on the session observes.</summary>
    public static void DriveBusState(string session, BusState state)
    {
        var field = typeof(VirtualBusHub).GetField("_hubs", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VirtualBusHub._hubs field not found.");
        var hubs = (IDictionary)field.GetValue(null)!;
        var hub = hubs[session] as VirtualBusHub
            ?? throw new InvalidOperationException($"No VirtualBusHub for session '{session}'.");
        hub.SetBusState(state);
    }

    /// <summary>Reports a Fault-severity exception through the bus's own exception dispatcher,
    /// which raises <c>ICanBus.FaultOccurred</c> exactly like a real adapter fault would.</summary>
    public static void DriveFault(ICanBus bus, Exception exception)
    {
        var field = bus.GetType().GetField("_exceptions", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{bus.GetType().Name}._exceptions field not found.");
        var dispatcher = (CanBusExceptionDispatcher)field.GetValue(bus)!;
        dispatcher.Report(exception, CanExceptionSource.Unknown, CanExceptionSeverity.Fault);
    }
}
