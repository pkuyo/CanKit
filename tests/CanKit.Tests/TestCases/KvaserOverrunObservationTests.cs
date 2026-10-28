#if FAKE
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Kvaser;
using CanKit.Adapter.Kvaser.Exceptions;
using CanKit.Adapter.Kvaser.Native;
using CanKit.Core;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Tests.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Exercises read flags, counter availability, event ordering and shutdown with a per-handle
/// CANlib fake. Native acceptance is modeled; hardware flag retention is not validated.
/// </summary>
public class KvaserOverrunObservationTests
{
    [Theory]
    [InlineData(false, Canlib.canMSGERR_HW_OVERRUN)]
    [InlineData(false, Canlib.canMSGERR_SW_OVERRUN)]
    [InlineData(false, Canlib.canMSGERR_OVERRUN)]
    [InlineData(true, Canlib.canMSGERR_HW_OVERRUN)]
    [InlineData(true, Canlib.canMSGERR_SW_OVERRUN)]
    [InlineData(true, Canlib.canMSGERR_OVERRUN)]
    public async Task Overrun_On_A_Data_Frame_Reports_Loss_And_Preserves_The_Frame(bool fd, int overrun)
    {
        using var bus = Open(fd);
        var errors = new ConcurrentQueue<ICanErrorInfo>();
        var observed = new ConcurrentQueue<int>();
        var receivedEvents = new ConcurrentQueue<int>();
        var deliveryOrder = new ConcurrentQueue<string>();
        bus.ErrorFrameReceived += (_, error) =>
        {
            errors.Enqueue(error);
            deliveryOrder.Enqueue("diagnostic");
        };
        bus.FrameObserved += (_, frame) =>
        {
            observed.Enqueue(frame.CanFrame.ID);
            deliveryOrder.Enqueue("observed");
        };
#pragma warning disable CS0618
        bus.FrameReceived += (_, frame) =>
        {
            receivedEvents.Enqueue(frame.CanFrame.ID);
            deliveryOrder.Enqueue("received");
        };
#pragma warning restore CS0618
        var payload = Enumerable.Range(1, fd ? 12 : 8).Select(i => (byte)i).ToArray();
        var flags = overrun | Canlib.canMSG_EXT | (fd ? Canlib.canFDMSG_FDF | Canlib.canFDMSG_BRS : 0);
        var pending = bus.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);

        Canlib.InjectReceiveFrame(bus.Handle, 0x12345, payload, flags, 7);

        var received = await pending;
        var frame = received.Should().ContainSingle().Subject.CanFrame;
        frame.ID.Should().Be(0x12345);
        frame.Data.ToArray().Should().Equal(payload);
        frame.IsExtendedFrame.Should().BeTrue();
        frame.IsErrorFrame.Should().BeFalse();
        frame.FrameKind.Should().Be(fd ? CanFrameType.CanFd : CanFrameType.Can20);
        frame.BitRateSwitch.Should().Be(fd);
        observed.Should().Equal(0x12345);
        receivedEvents.Should().Equal(0x12345);
        deliveryOrder.Should().Equal("diagnostic", "received", "observed");
        var error = errors.Should().ContainSingle().Subject;
        error.Type.Should().Be(FrameErrorType.Controller);
        error.ControllerStatus.Should().HaveFlag(CanControllerStatus.RxOverflow);
        error.RawErrorCode.Should().Be((uint)flags);
        error.Direction.Should().Be(FrameDirection.Rx);
        error.DeviceTimeSpan.Should().Be(received[0].ReceiveTimestamp);
        error.Frame.Should().BeNull("the diagnostic does not borrow the data frame's buffer");
    }

    [Theory]
    [InlineData(false, Canlib.canMSGERR_OVERRUN)]
    [InlineData(true, Canlib.canMSGERR_OVERRUN)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public async Task An_Error_Frame_Produces_One_Diagnostic_And_No_Data(bool fd, int overrun)
    {
        using var bus = Open(fd);
        var errors = new ConcurrentQueue<ICanErrorInfo>();
        var observed = new ConcurrentQueue<int>();
        bus.ErrorFrameReceived += (_, error) => errors.Enqueue(error);
        bus.FrameObserved += (_, frame) => observed.Enqueue(frame.CanFrame.ID);
        var pending = bus.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);
        var flags = overrun | Canlib.canMSG_ERROR_FRAME | (fd ? Canlib.canFDMSG_FDF : 0);

        Canlib.InjectReceiveFrame(bus.Handle, 0, new byte[8], flags, 11);
        Canlib.InjectReceiveFrame(bus.Handle, 0x321, new byte[] { 9 }, 0);

        (await pending).Should().ContainSingle().Which.CanFrame.ID.Should().Be(0x321);
        observed.Should().Equal(0x321);
        var error = errors.Should().ContainSingle().Subject;
        ((error.ControllerStatus & CanControllerStatus.RxOverflow) != 0).Should().Be(overrun != 0);
        error.Type.Should().Be(overrun != 0 ? FrameErrorType.Controller : FrameErrorType.Unknown);
        error.RawErrorCode.Should().Be((uint)flags);
        error.Direction.Should().Be(FrameDirection.Rx);
        error.DeviceTimeSpan.Should().Be(TimeSpan.FromTicks(11 * bus.Options.TimerScaleMicroseconds * 10L));
    }

    [Fact]
    public async Task Repeated_Indications_Are_Reported_Without_Inventing_A_Recovery()
    {
        using var bus = Open(false);
        var errors = new ConcurrentQueue<ICanErrorInfo>();
        bus.ErrorFrameReceived += (_, error) => errors.Enqueue(error);
        var pending = bus.ReceiveAsync(3, 5000, TestContext.Current.CancellationToken);

        Canlib.InjectReceiveFrame(bus.Handle, 0x101, new byte[] { 1 }, Canlib.canMSGERR_HW_OVERRUN);
        Canlib.InjectReceiveFrame(bus.Handle, 0x102, new byte[] { 2 }, Canlib.canMSGERR_HW_OVERRUN);
        Canlib.InjectReceiveFrame(bus.Handle, 0x103, new byte[] { 3 }, Canlib.canMSG_EXT);

        (await pending).Select(r => r.CanFrame.ID).Should().Equal(0x101, 0x102, 0x103);
        errors.Should().HaveCount(2);
        errors.Should().OnlyContain(e => (e.ControllerStatus & CanControllerStatus.RxOverflow) != 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Data_Is_Delivered_Without_Error_Subscribers(bool errorInfoEnabled)
    {
        using var bus = Open(false, errorInfoEnabled);
        var pending = bus.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);

        Canlib.InjectReceiveFrame(bus.Handle, 0x123, new byte[] { 1 }, Canlib.canMSGERR_SW_OVERRUN);

        (await pending).Should().ContainSingle().Which.CanFrame.ID.Should().Be(0x123);
        if (!errorInfoEnabled)
        {
            var subscribe = () => bus.ErrorFrameReceived += (_, _) => { };
            subscribe.Should().Throw<CanBusConfigurationException>();
        }
    }

    [Fact]
    public async Task A_Throwing_Diagnostic_Subscriber_Uses_The_Exception_Policy_And_Data_Continues()
    {
        using var bus = Open(false);
        var reported = new ConcurrentQueue<Exception>();
        var faults = new ConcurrentQueue<Exception>();
        bus.BackgroundExceptionOccurred += (_, error) => reported.Enqueue(error);
        bus.FaultOccurred += (_, error) => faults.Enqueue(error);
        bus.ErrorFrameReceived += (_, _) => throw new InvalidOperationException("diagnostic subscriber failed");
        var pending = bus.ReceiveAsync(2, 5000, TestContext.Current.CancellationToken);

        Canlib.InjectReceiveFrame(bus.Handle, 0x101, new byte[] { 1 }, Canlib.canMSGERR_HW_OVERRUN);
        Canlib.InjectReceiveFrame(bus.Handle, 0x102, new byte[] { 2 }, 0);

        (await pending).Select(r => r.CanFrame.ID).Should().Equal(0x101, 0x102);
        reported.Should().ContainSingle().Which.Should().BeOfType<InvalidOperationException>();
        faults.Should().BeEmpty();
    }

    [Fact]
    public async Task Overrun_Diagnostics_Are_Forwarded_Through_QueuedTx()
    {
        using var inner = Open(false);
        using var bus = inner.WithQueuedTx();
        var errors = new ConcurrentQueue<ICanErrorInfo>();
        bus.ErrorFrameReceived += (_, error) => errors.Enqueue(error);
        var pending = bus.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);

        Canlib.InjectReceiveFrame(inner.Handle, 0x123, new byte[] { 1 }, Canlib.canMSGERR_HW_OVERRUN);

        (await pending).Should().ContainSingle().Which.CanFrame.ID.Should().Be(0x123);
        errors.Should().ContainSingle().Which.ControllerStatus.Should().HaveFlag(CanControllerStatus.RxOverflow);
    }

    [Fact]
    public async Task Removing_The_Diagnostic_Subscriber_Does_Not_Stop_Data_Reception()
    {
        using var bus = Open(false);
        var errors = new ConcurrentQueue<ICanErrorInfo>();
        EventHandler<ICanErrorInfo> handler = (_, error) => errors.Enqueue(error);
        bus.ErrorFrameReceived += handler;
        var first = bus.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);
        Canlib.InjectReceiveFrame(bus.Handle, 0x101, new byte[] { 1 }, Canlib.canMSGERR_HW_OVERRUN);
        (await first).Should().ContainSingle();
        errors.Should().ContainSingle();

        bus.ErrorFrameReceived -= handler;
        var second = bus.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);
        Canlib.InjectReceiveFrame(bus.Handle, 0x102, new byte[] { 2 }, Canlib.canMSGERR_SW_OVERRUN);

        (await second).Should().ContainSingle().Which.CanFrame.ID.Should().Be(0x102);
        errors.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, Canlib.canMSGERR_HW_OVERRUN)]
    [InlineData(true, Canlib.canMSGERR_HW_OVERRUN)]
    [InlineData(false, Canlib.canMSG_ERROR_FRAME)]
    [InlineData(true, Canlib.canMSG_ERROR_FRAME)]
    public async Task Optional_Counter_Failure_Omits_Counters_And_Preserves_Reception(bool fd, int flags)
    {
        using var bus = Open(fd);
        var diagnostics = new ConcurrentQueue<ICanErrorInfo>();
        var faults = new ConcurrentQueue<Exception>();
        var background = new ConcurrentQueue<Exception>();
        bus.ErrorFrameReceived += (_, error) => diagnostics.Enqueue(error);
        bus.FaultOccurred += (_, error) => faults.Enqueue(error);
        bus.BackgroundExceptionOccurred += (_, error) => background.Enqueue(error);
        Canlib.SetErrorCountersResult(bus.Handle, Canlib.canStatus.canERR_DRIVER);
        var readCounters = () => bus.ErrorCounters();
        readCounters.Should().Throw<KvaserCanException>().Which.Operation.Should().Be("canReadErrorCounters");
        var expected = (flags & Canlib.canMSG_ERROR_FRAME) != 0 ? new[] { 0x321 } : new[] { 0x123, 0x321 };
        var pending = bus.ReceiveAsync(expected.Length, 5000, TestContext.Current.CancellationToken);
        var frameFlags = fd ? Canlib.canFDMSG_FDF : 0;

        Canlib.InjectReceiveFrame(bus.Handle, 0x123, new byte[] { 1 }, frameFlags | flags);
        Canlib.InjectReceiveFrame(bus.Handle, 0x321, new byte[] { 2 }, frameFlags);

        (await pending).Select(r => r.CanFrame.ID).Should().Equal(expected);
        var degraded = diagnostics.Should().ContainSingle().Subject;
        degraded.ErrorCounters.Should().BeNull();
        degraded.ControllerStatus.Should().HaveFlag(CanControllerStatus.Unknown);
        ((degraded.ControllerStatus & CanControllerStatus.RxOverflow) != 0)
            .Should().Be((flags & Canlib.canMSGERR_OVERRUN) != 0);
        degraded.RawErrorCode.Should().Be((uint)(frameFlags | flags));

        Canlib.SetErrorCountersResult(bus.Handle, Canlib.canStatus.canOK);
        var next = bus.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);
        Canlib.InjectReceiveFrame(bus.Handle, 0x456, new byte[] { 3 }, frameFlags | Canlib.canMSGERR_HW_OVERRUN);

        (await next).Should().ContainSingle().Which.CanFrame.ID.Should().Be(0x456);
        diagnostics.Should().HaveCount(2);
        diagnostics.Last().ErrorCounters.Should().NotBeNull();
        faults.Should().BeEmpty();
        background.Should().BeEmpty("unavailable optional counters are represented by null, not a receive failure");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Echo_Acknowledgment_Preserves_Overrun_Diagnostics_And_Echo_Metadata(bool fd)
    {
        using var bus = Open(fd, configure: cfg => cfg.SetWorkMode(ChannelWorkMode.Echo));
        var errors = new ConcurrentQueue<ICanErrorInfo>();
        bus.ErrorFrameReceived += (_, error) => errors.Enqueue(error);
        var flags = Canlib.canMSG_TXACK | Canlib.canMSGERR_HW_OVERRUN | (fd ? Canlib.canFDMSG_FDF : 0);
        var pending = bus.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);

        Canlib.InjectReceiveFrame(bus.Handle, 0x123, new byte[] { 1 }, flags);

        var received = (await pending).Should().ContainSingle().Subject;
        received.IsEcho.Should().BeTrue();
        received.CanFrame.ID.Should().Be(0x123);
        var error = errors.Should().ContainSingle().Subject;
        error.ControllerStatus.Should().HaveFlag(CanControllerStatus.RxOverflow);
        error.RawErrorCode.Should().Be((uint)flags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Software_Range_Filter_Drops_Data_But_Preserves_Observed_Overrun(bool fd)
    {
        using var bus = Open(fd, configure: cfg =>
            cfg.SoftwareFeaturesFallBack(CanFeature.RangeFilter)
                .RangeFilter(0x200, 0x2FF, CanFilterIDType.Standard));
        var errors = new ConcurrentQueue<ICanErrorInfo>();
        var observed = new ConcurrentQueue<int>();
        bus.ErrorFrameReceived += (_, error) => errors.Enqueue(error);
        bus.FrameObserved += (_, frame) => observed.Enqueue(frame.CanFrame.ID);
        var pending = bus.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);
        var frameFlags = fd ? Canlib.canFDMSG_FDF : 0;

        Canlib.InjectReceiveFrame(bus.Handle, 0x100, new byte[] { 1 }, frameFlags | Canlib.canMSGERR_SW_OVERRUN);
        Canlib.InjectReceiveFrame(bus.Handle, 0x200, new byte[] { 2 }, frameFlags);

        (await pending).Should().ContainSingle().Which.CanFrame.ID.Should().Be(0x200);
        observed.Should().Equal(0x200);
        errors.Should().ContainSingle().Which.ControllerStatus.Should().HaveFlag(CanControllerStatus.RxOverflow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configured_Acceptance_Filter_Is_Applied_By_The_Fake_Native_Backend(bool fd)
    {
        using var bus = Open(fd, configure: cfg => cfg.AccMask(0x200, 0x7FF, CanFilterIDType.Standard));
        var errors = new ConcurrentQueue<ICanErrorInfo>();
        bus.ErrorFrameReceived += (_, error) => errors.Enqueue(error);
        var pending = bus.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);
        var frameFlags = fd ? Canlib.canFDMSG_FDF : 0;

        // The fake models configured native acceptance; no claim about SDK flag retention.
        Canlib.InjectReceiveFrame(bus.Handle, 0x100, new byte[] { 1 }, frameFlags | Canlib.canMSGERR_HW_OVERRUN).Should().BeFalse();
        Canlib.InjectReceiveFrame(bus.Handle, 0x200, new byte[] { 2 }, frameFlags | Canlib.canMSGERR_HW_OVERRUN).Should().BeTrue();

        (await pending).Should().ContainSingle().Which.CanFrame.ID.Should().Be(0x200);
        errors.Should().ContainSingle().Which.RawErrorCode.Should().Be((uint)(frameFlags | Canlib.canMSGERR_HW_OVERRUN));
    }

    [Theory]
    [InlineData(false, Canlib.canMSGERR_HW_OVERRUN, false, false)]
    [InlineData(true, Canlib.canMSGERR_HW_OVERRUN, false, false)]
    [InlineData(false, Canlib.canMSG_ERROR_FRAME, false, false)]
    [InlineData(true, Canlib.canMSG_ERROR_FRAME, false, false)]
    [InlineData(false, Canlib.canMSGERR_HW_OVERRUN, true, false)]
    [InlineData(true, Canlib.canMSGERR_HW_OVERRUN, true, false)]
    [InlineData(false, Canlib.canMSGERR_HW_OVERRUN, false, true)]
    [InlineData(true, Canlib.canMSGERR_HW_OVERRUN, false, true)]
    [InlineData(false, Canlib.canMSGERR_HW_OVERRUN, true, true)]
    [InlineData(true, Canlib.canMSGERR_HW_OVERRUN, true, true)]
    public async Task Disposal_During_A_Native_Read_Does_Not_Report_A_Fault(bool fd, int flags, bool exposeCancellation, bool readFails)
    {
        using var bus = Open(fd, configure: cfg => cfg.ExceptionPolicy(new CanExceptionPolicy
        {
            IgnoreOperationCanceledException = !exposeCancellation
        }));
        using var releaseRead = new ManualResetEventSlim();
        var readEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var faults = new ConcurrentQueue<Exception>();
        var background = new ConcurrentQueue<Exception>();
        bus.ErrorFrameReceived += (_, _) => { };
        bus.FaultOccurred += (_, error) => faults.Enqueue(error);
        bus.BackgroundExceptionOccurred += (_, error) => background.Enqueue(error);
        if (readFails) Canlib.SetReadResult(bus.Handle, Canlib.canStatus.canERR_INVHANDLE);
        Canlib.SetReadHook(bus.Handle, () =>
        {
            readEntered.TrySetResult(true);
            if (!releaseRead.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Native read was not released.");
        });
        try
        {
            Canlib.InjectReceiveFrame(bus.Handle, 0x123, new byte[] { 1 }, flags | (fd ? Canlib.canFDMSG_FDF : 0));
            await CompleteWithin(readEntered.Task);
            bus.Dispose();
        }
        finally
        {
            releaseRead.Set();
            if (bus.ReceiveLoopTask is { } loop) await CompleteWithin(loop);
        }

        faults.Should().BeEmpty();
        if (exposeCancellation)
        {
            var cancellation = background.Should().ContainSingle().Subject.Should().BeOfType<OperationCanceledException>().Which;
            cancellation.CancellationToken.IsCancellationRequested.Should().BeTrue();
        }
        else
        {
            background.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task Disposal_At_Delivery_Boundaries_Returns_An_Unpublished_Frame(bool fd, bool disposeDuringRent, bool exposeCancellation)
    {
        using var releaseRent = new ManualResetEventSlim();
        var rentEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allocator = new CountingBufferAllocator(() =>
        {
            if (!disposeDuringRent) return;
            rentEntered.TrySetResult(true);
            if (!releaseRent.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Buffer rent was not released.");
        });
        using var bus = Open(fd, configure: cfg =>
        {
            cfg.BufferAllocator(allocator);
            cfg.ExceptionPolicy(new CanExceptionPolicy { IgnoreOperationCanceledException = !exposeCancellation });
        });
        var faults = new ConcurrentQueue<Exception>();
        var background = new ConcurrentQueue<Exception>();
        var observed = new ConcurrentQueue<int>();
        bus.ErrorFrameReceived += (_, _) => { if (!disposeDuringRent) bus.Dispose(); };
        bus.FaultOccurred += (_, error) => faults.Enqueue(error);
        bus.BackgroundExceptionOccurred += (_, error) => background.Enqueue(error);
        bus.FrameObserved += (_, frame) => observed.Enqueue(frame.CanFrame.ID);
        try
        {
            Canlib.InjectReceiveFrame(bus.Handle, 0x123, new byte[] { 1 },
                Canlib.canMSGERR_HW_OVERRUN | (fd ? Canlib.canFDMSG_FDF : 0));
            if (disposeDuringRent)
            {
                await CompleteWithin(rentEntered.Task);
                bus.Dispose();
            }
        }
        finally
        {
            releaseRent.Set();
            if (bus.ReceiveLoopTask is { } loop) await CompleteWithin(loop);
        }

        if (disposeDuringRent) allocator.Rented.Should().Be(1);
        allocator.Returned.Should().Be(allocator.Rented);
        observed.Should().BeEmpty();
        faults.Should().BeEmpty();
        if (exposeCancellation)
        {
            var cancellation = background.Should().ContainSingle().Subject.Should().BeOfType<OperationCanceledException>().Which;
            cancellation.CancellationToken.IsCancellationRequested.Should().BeTrue();
            cancellation.CancellationToken.Should().NotBe(new CancellationToken(true), "the drain retains its own CTS token");
        }
        else
        {
            background.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_Read_Failure_Still_Faults_The_Receive_Loop(bool fd)
    {
        using var bus = Open(fd);
        var diagnostics = new ConcurrentQueue<ICanErrorInfo>();
        var faults = new ConcurrentQueue<Exception>();
        var background = new ConcurrentQueue<Exception>();
        bus.ErrorFrameReceived += (_, error) => diagnostics.Enqueue(error);
        bus.FaultOccurred += (_, error) => faults.Enqueue(error);
        bus.BackgroundExceptionOccurred += (_, error) => background.Enqueue(error);
        Canlib.SetReadResult(bus.Handle, Canlib.canStatus.canERR_DRIVER);
        var pending = bus.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);

        Canlib.InjectReceiveFrame(bus.Handle, 0x123, new byte[] { 1 }, Canlib.canMSGERR_HW_OVERRUN);

        var failure = (await FluentActions.Awaiting(() => pending).Should().ThrowAsync<KvaserCanException>()).Which;
        failure.Operation.Should().Be("canRead");
        await CompleteWithin(bus.ReceiveLoopTask!);
        faults.Should().ContainSingle().Which.Should().BeSameAs(failure);
        background.Should().ContainSingle().Which.Should().BeSameAs(failure);
        diagnostics.Should().BeEmpty();

        Canlib.SetReadResult(bus.Handle, Canlib.canStatus.canOK);
        bus.Reset();
        Canlib.GetNotificationMask(bus.Handle).Should().Be(0, "Reset does not restart the stopped receiver");
        Canlib.InjectReceiveFrame(bus.Handle, 0x321, new byte[] { 2 }, 0);
        using var cancelLaterRead = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var laterRead = bus.ReceiveAsync(1, -1, cancelLaterRead.Token);
        await Task.Delay(25, TestContext.Current.CancellationToken);
        laterRead.IsCompleted.Should().BeFalse("the earlier exception was a one-shot notification");
        cancelLaterRead.Cancel();
        (await laterRead).Should().BeEmpty();

        bus.Dispose();
        using var reopened = Open(fd);
        var afterReopen = reopened.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);
        Canlib.InjectReceiveFrame(reopened.Handle, 0x456, new byte[] { 3 }, fd ? Canlib.canFDMSG_FDF : 0);
        (await afterReopen).Should().ContainSingle().Which.CanFrame.ID.Should().Be(0x456);
    }

    private static async Task CompleteWithin(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        completed.Should().BeSameAs(task);
        await task;
    }

    private static KvaserBus Open(bool fd, bool errorInfoEnabled = true, Action<KvaserBusInitConfigurator>? configure = null)
        => Kvaser.Open(0, cfg =>
        {
            cfg.SetProtocolMode(fd ? CanProtocolMode.CanFd : CanProtocolMode.Can20);
            if (fd) cfg.Fd(500_000, 2_000_000);
            else cfg.Baud(500_000);
            if (errorInfoEnabled) cfg.EnableErrorInfo();
            cfg.ExceptionPolicy(new CanExceptionPolicy { SubscriberCallbackSeverity = CanExceptionSeverity.Error });
            configure?.Invoke(cfg);
        });
}
#endif
