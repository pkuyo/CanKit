#if FAKE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.ControlCAN;
using CanKit.Core;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public partial class ControlCanFilterRulesTests
{
    [Theory]
    [InlineData(0, false)] // single frame
    [InlineData(1, false)] // enumerable
    [InlineData(2, false)] // span
    [InlineData(3, false)] // array
    [InlineData(4, false)] // array segment
    [InlineData(5, false)] // async enumerable
    [InlineData(6, false)] // async single frame
    [InlineData(0, true)] // terminal receive fault while transmitting
    public async Task Native_Transmit_Retains_Channel_Until_It_Returns(int overload, bool receiveFault)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var bus = (ControlCanBus)CanBus.Open(RangeRx, cfg => Configure(cfg)
            .RangeFilter(0x100, 0x10F, CanFilterIDType.Standard)
            .ExceptionPolicy(new CanExceptionPolicy { SubscriberCallbackSeverity = CanExceptionSeverity.Fault }));
        using var peer = CanBus.Open(RangeTx, cfg => Configure(cfg));
        var owner = TrackDeviceLease(bus);
        var polling = (Task)typeof(ControlCanBus).GetField("_pollTask", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(bus)!;
        if (receiveFault) bus.FrameObserved += (_, _) => throw new InvalidOperationException("Injected receive fault during TX");
        InvokeNative("SetTransmitHook", 21U, 2U, 0U, (Action)(() =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }));
        var frames = Enumerable.Range(0, 65).Select(i => CanFrame.Classic(0x105, new byte[] { (byte)i })).ToArray();
        Task<int>? sending = null;
        try
        {
            sending = Task.Run(() => TransmitUsingOverload(bus, frames, overload), TestContext.Current.CancellationToken);
            entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
            if (receiveFault)
            {
                peer.Transmit(CanFrame.Classic(0x106)).Should().Be(1);
                (await Task.WhenAny(polling, Task.Delay(5000, TestContext.Current.CancellationToken)))
                    .Should().BeSameAs(polling);
                await polling;
            }
            else
            {
                var disposing = Task.Run(bus.Dispose, TestContext.Current.CancellationToken);
                (await Task.WhenAny(disposing, Task.Delay(5000, TestContext.Current.CancellationToken)))
                    .Should().BeSameAs(disposing, "shutdown defers cleanup without waiting on a blocked driver");
                await disposing;
            }

            owner.DisposeCount.Should().Be(0, "the native send still owns the device");
            InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(true);
            InvokeNative("ResetCount", 21U, 2U, 0U).Should().Be(1);
            InvokeNative("ActiveRangeCount", 21U, 2U, 0U).Should().Be(1);
            Action anotherSend = () => TransmitUsingOverload(bus, frames, overload).GetAwaiter().GetResult();
            anotherSend.Should().Throw<CanBusDisposedException>();
            Action reopen = () => { using var duplicate = CanBus.Open(RangeRx, cfg => Configure(cfg)); };
            reopen.Should().Throw<CanBusCreationException>().WithMessage("*already open or still stopping*");

            release.Set();
            var expected = overload is 0 or 6 ? 1 : frames.Length;
            (await sending).Should().Be(expected);
            WaitForChannelCleanup(bus);
            owner.DisposeCount.Should().Be(1);
            InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(false);
            InvokeNative("ActiveRangeCount", 21U, 2U, 0U).Should().Be(0);
            (await peer.ReceiveAsync(expected, 1000, TestContext.Current.CancellationToken)).Should().HaveCount(expected);
        }
        finally
        {
            release.Set();
            if (sending != null) await sending;
            InvokeNative("SetTransmitHook", 21U, 2U, 0U, null!);
        }
    }

    private static Task<int> TransmitUsingOverload(ControlCanBus bus, CanFrame[] frames, int overload) => overload switch
    {
        0 => Task.FromResult(bus.Transmit(frames[0])),
        1 => Task.FromResult(bus.Transmit((IEnumerable<CanFrame>)frames)),
        2 => Task.FromResult(bus.Transmit(frames.AsSpan())),
        3 => Task.FromResult(bus.Transmit(frames)),
        4 => Task.FromResult(bus.Transmit(new ArraySegment<CanFrame>(frames))),
        5 => bus.TransmitAsync((IEnumerable<CanFrame>)frames),
        6 => bus.TransmitAsync(frames[0]),
        _ => throw new ArgumentOutOfRangeException(nameof(overload))
    };

    [Fact]
    public async Task Cleanup_Waits_For_All_Concurrent_Transmits()
    {
        using var entered = new CountdownEvent(2);
        using var release = new SemaphoreSlim(0);
        using var bus = (ControlCanBus)CanBus.Open(RangeRx, cfg => Configure(cfg));
        using var peer = CanBus.Open(RangeTx, cfg => Configure(cfg));
        var owner = TrackDeviceLease(bus);
        InvokeNative("SetTransmitHook", 21U, 2U, 0U, (Action)(() =>
        {
            entered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
                throw new TimeoutException("Test did not release native transmission");
        }));
        var first = Task.Run(() => bus.Transmit(CanFrame.Classic(0x105)), TestContext.Current.CancellationToken);
        var second = Task.Run(() => bus.Transmit(CanFrame.Classic(0x106)), TestContext.Current.CancellationToken);
        try
        {
            entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
            bus.Dispose();
            owner.DisposeCount.Should().Be(0);
            release.Release();
            var finished = await Task.WhenAny(first, second);
            (await finished).Should().Be(1);
            owner.DisposeCount.Should().Be(0, "another native transmit is still active");
            InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(true);
            release.Release();
            await Task.WhenAll(first, second);
            WaitForChannelCleanup(bus);
            owner.DisposeCount.Should().Be(1);
        }
        finally
        {
            release.Release(2);
            await Task.WhenAll(first, second);
            InvokeNative("SetTransmitHook", 21U, 2U, 0U, null!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_During_Transmit_Enumeration_Releases_Resources_After_The_Call(bool throwDuringEnumeration)
    {
        using var bus = (ControlCanBus)CanBus.Open(RangeRx, cfg => Configure(cfg));
        using var peer = CanBus.Open(RangeTx, cfg => Configure(cfg));
        var owner = TrackDeviceLease(bus);
        var failure = new InvalidOperationException("Injected enumeration failure");
        IEnumerable<CanFrame> Frames()
        {
            yield return CanFrame.Classic(0x105);
            bus.Dispose();
            owner.DisposeCount.Should().Be(0);
            InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(true);
            if (throwDuringEnumeration) throw failure;
            yield return CanFrame.Classic(0x106);
        }
        if (throwDuringEnumeration)
        {
            Action send = () => bus.Transmit(Frames());
            send.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        }
        else
        {
            bus.Transmit(Frames()).Should().Be(2);
            (await peer.ReceiveAsync(2, 1000, TestContext.Current.CancellationToken))
                .Select(frame => frame.CanFrame.ID).Should().Equal(0x105, 0x106);
        }
        WaitForChannelCleanup(bus);
        owner.DisposeCount.Should().Be(1);
        InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(false);
    }

    [Fact]
    public void Periodic_Transmit_Cannot_Program_A_Channel_After_Its_Bus_Is_Disposed()
    {
        using var bus = CanBus.Open(RangeRx, cfg => Configure(cfg));
        using var peer = CanBus.Open(RangeTx, cfg => Configure(cfg));
        using var periodic = bus.TransmitPeriodic(CanFrame.Classic(0x105),
            new PeriodicTxOptions(TimeSpan.FromSeconds(10), -1, fireImmediately: false));
        bus.Dispose();
        using var reopened = CanBus.Open(RangeRx, cfg => Configure(cfg));
        Action update = () => periodic.Update(frame: CanFrame.Classic(0x106));
        update.Should().Throw<CanBusDisposedException>();
        Action dispose = periodic.Dispose;
        dispose.Should().NotThrow("returning a managed auto-send slot is safe after bus disposal");
    }

    private static TrackingOwner TrackDeviceLease(ControlCanBus bus)
    {
        var ownerField = typeof(ControlCanBus).GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var owner = new TrackingOwner((IDisposable)ownerField.GetValue(bus)!);
        bus.AttachOwner(owner);
        return owner;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Channel_Cleanup_Disables_Auto_Send_Even_After_A_Failed_Stop(bool receiveFault, bool failedStop)
    {
        using var bus = (ControlCanBus)CanBus.Open(RangeRx, cfg => Configure(cfg)
            .ExceptionPolicy(new CanExceptionPolicy { SubscriberCallbackSeverity = CanExceptionSeverity.Fault }));
        using var peer = CanBus.Open(RangeTx, cfg => Configure(cfg));
        using var first = bus.TransmitPeriodic(CanFrame.Classic(0x105), new PeriodicTxOptions(TimeSpan.FromSeconds(10), -1, false));
        using var second = bus.TransmitPeriodic(CanFrame.Classic(0x106), new PeriodicTxOptions(TimeSpan.FromSeconds(10), -1, false));
        InvokeNative("AutoSendCount", 21U, 2U, 0U).Should().Be(2);
        if (failedStop)
        {
            InvokeNative("RejectReference", 21U, 2U, 0U, 5U);
            try { first.Dispose(); }
            finally { InvokeNative("AllowReference", 21U, 2U, 0U, 5U); }
        }
        else
        {
            first.Stop();
            InvokeNative("AutoSendCount", 21U, 2U, 0U).Should().Be(1, "stopping one entry must preserve the other");
        }
        using var replacement = bus.TransmitPeriodic(CanFrame.Classic(0x107), new PeriodicTxOptions(TimeSpan.FromSeconds(10), -1, false));
        InvokeNative("AutoSendCount", 21U, 2U, 0U).Should().Be(failedStop ? 3 : 2,
            "an uncertain native entry must not have its index reused");
        if (receiveFault)
        {
            bus.FrameObserved += (_, _) => throw new InvalidOperationException("Injected periodic TX receive fault");
            peer.Transmit(CanFrame.Classic(0x200)).Should().Be(1);
        }
        else bus.Dispose();
        WaitForChannelCleanup(bus);
        InvokeNative("AutoSendCount", 21U, 2U, 0U).Should().Be(0,
            "ResetCAN alone leaves the fake firmware's auto-send table intact");
        using var reopened = CanBus.Open(RangeRx, cfg => Configure(cfg));
        InvokeNative("AutoSendCount", 21U, 2U, 0U).Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Failed_Periodic_Stop_Can_Be_Retried_Without_Disposing_The_Bus(bool firstDispose, bool retryDispose)
    {
        using var bus = CanBus.Open(RangeRx, cfg => Configure(cfg));
        using var periodic = bus.TransmitPeriodic(CanFrame.Classic(0x105),
            new PeriodicTxOptions(TimeSpan.FromSeconds(10), -1, false));
        InvokeNative("RejectReference", 21U, 2U, 0U, 5U);
        try
        {
            if (firstDispose) periodic.Dispose();
            else periodic.Stop();
            InvokeNative("AutoSendCount", 21U, 2U, 0U).Should().Be(1);
        }
        finally { InvokeNative("AllowReference", 21U, 2U, 0U, 5U); }

        if (retryDispose) periodic.Dispose();
        else periodic.Stop();
        InvokeNative("AutoSendCount", 21U, 2U, 0U).Should().Be(0,
            "a failed disable must remain retryable while the bus is open");
        InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(true);
        periodic.Dispose();

        using var replacement = bus.TransmitPeriodic(CanFrame.Classic(0x106),
            new PeriodicTxOptions(TimeSpan.FromSeconds(10), -1, false));
        periodic.Dispose();
        periodic.Stop();
        using var other = bus.TransmitPeriodic(CanFrame.Classic(0x107),
            new PeriodicTxOptions(TimeSpan.FromSeconds(10), -1, false));
        InvokeNative("AutoSendCount", 21U, 2U, 0U).Should().Be(2,
            "repeated disposal must neither disable a replacement nor release its reserved index");
    }
}
#endif
