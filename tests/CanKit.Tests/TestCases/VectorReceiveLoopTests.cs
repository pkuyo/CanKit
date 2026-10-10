#if FAKE
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Vector;
using CanKit.Core;
using CanKit.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Lifecycle of the Vector receive loop, run against the FAKE XL driver (polling mode).
/// </summary>
public class VectorReceiveLoopTests
{
    private const string EndpointA = "vector://virtual/0";
    private const string EndpointB = "vector://virtual/1";

    [Fact]
    public async Task Receive_Loop_Ends_When_The_Bus_Is_Disposed()
    {
        var bus = (VectorBus)CanBus.Open(EndpointA, Configure);
        var loop = bus.ReceiveLoopTask;
        loop.Should().NotBeNull();
        loop!.IsCompleted.Should().BeFalse("the loop runs while the bus is open");

        bus.Dispose();

        var finished = await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        finished.Should().BeSameAs(loop, "disposing the bus must stop the receive loop");
        loop.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_Does_Not_Report_A_Fault_Or_Background_Exception()
    {
        var reported = new List<Exception>();
        var bus = (VectorBus)CanBus.Open(EndpointA, Configure);
        bus.FaultOccurred += (_, ex) => { lock (reported) reported.Add(ex); };
        bus.BackgroundExceptionOccurred += (_, ex) => { lock (reported) reported.Add(ex); };
        var loop = bus.ReceiveLoopTask!;

        bus.Dispose();
        await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));

        lock (reported) reported.Should().BeEmpty();
    }

    [Fact]
    public async Task Frames_Are_Still_Delivered_By_The_Receive_Loop()
    {
        using var rx = CanBus.Open(EndpointA, Configure);
        using var tx = CanBus.Open(EndpointB, Configure);

        tx.Transmit(CanFrame.Classic(0x321, new byte[] { 1, 2, 3 })).Should().Be(1);

        var received = await rx.ReceiveAsync(1, 2000, TestContext.Current.CancellationToken);
        received.Should().ContainSingle().Which.CanFrame.ID.Should().Be(0x321);
    }

    [Fact]
    public async Task Fault_Raised_From_A_Subscriber_Callback_Stops_The_Loop_Without_A_Second_Report()
    {
        // With this policy a throwing subscriber faults the bus. The dispatcher then stops the
        // receive loop from the loop thread itself.
        var policy = new CanExceptionPolicy { SubscriberCallbackSeverity = CanExceptionSeverity.Fault };
        var faults = new List<Exception>();
        var background = new List<Exception>();

        using var rx = (VectorBus)CanBus.Open(EndpointA, cfg =>
        {
            Configure(cfg);
            cfg.ExceptionPolicy(policy);
        });
        using var tx = CanBus.Open(EndpointB, Configure);
        rx.FaultOccurred += (_, ex) => { lock (faults) faults.Add(ex); };
        rx.BackgroundExceptionOccurred += (_, ex) => { lock (background) background.Add(ex); };
        rx.FrameObserved += (_, _) => throw new InvalidOperationException("subscriber failed");
        var loop = rx.ReceiveLoopTask!;

        tx.Transmit(CanFrame.Classic(0x321, new byte[] { 1 })).Should().Be(1);

        var finished = await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        finished.Should().BeSameAs(loop, "a fault stops the receive loop");
        loop.IsFaulted.Should().BeFalse();

        lock (faults)
            faults.Should().ContainSingle().Which.Should().BeOfType<InvalidOperationException>();
        lock (background)
            background.Should().ContainSingle("the loop must not report a second exception while it leaves")
                .Which.Should().BeOfType<InvalidOperationException>();
    }

    private static void Configure(CanKit.Abstractions.API.Common.IBusInitOptionsConfigurator cfg)
        => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000).SetAsyncBufferCapacity(256);
}
#endif
