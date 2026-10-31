#if FAKE
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Adapter.ControlCAN;
using CanKit.Adapter.ControlCAN.Diagnostics;
using CanKit.Core;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Filter rules are combined with OR. The hardware takes the list only when it can express it as
/// a whole; any other list is evaluated in software with the hardware filter left open, so the
/// OR is preserved. Building that list must not modify the rule list being iterated.
/// </summary>
public partial class ControlCanFilterRulesTests
{
    // Dedicated fake devices; index 0 belongs to the matrix, index 1 to ControlCanRangeFilterTests.
    private const string RangeRx = "controlcan://VCI_USBCAN_2E_U?index=2#ch0";
    private const string RangeTx = "controlcan://VCI_USBCAN_2E_U?index=2#ch1";
    private const string MaskRx = "controlcan://VCI_USBCAN2?index=2#ch0";
    private const string MaskTx = "controlcan://VCI_USBCAN2?index=2#ch1";

    [Theory]
    [InlineData(CanExceptionSeverity.Error)]
    [InlineData(CanExceptionSeverity.Fault)]
    public async Task Throwing_Software_Predicate_Releases_The_Frame_And_Respects_Exception_Policy(CanExceptionSeverity severity)
    {
        var allocator = new CountingBufferAllocator();
        var failure = new InvalidOperationException("Injected filter failure");
        var filter = new CanFilter();
        filter.SoftwareFilterRules.Add(new PredicateRule(frame => frame.ID == 0x100 ? throw failure : true));
        using var rx = CanBus.Open(MaskRx, cfg => Configure(cfg).BufferAllocator(allocator).SetFilter(filter)
            .ExceptionPolicy(new CanExceptionPolicy { SubscriberCallbackSeverity = severity }));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var faults = 0;
        rx.BackgroundExceptionOccurred += (_, ex) => reported.TrySetResult(ex);
        rx.FaultOccurred += (_, _) => Interlocked.Increment(ref faults);
        var receiving = rx.ReceiveAsync(63, 2000, TestContext.Current.CancellationToken);
        tx.Transmit(Enumerable.Range(0, 64).Select(i => CanFrame.Classic(0x100 + i, new byte[] { 1 })).ToArray())
            .Should().Be(64);
        (await Task.WhenAny(reported.Task, Task.Delay(5000, TestContext.Current.CancellationToken)))
            .Should().BeSameAs(reported.Task);
        (await reported.Task).Should().BeSameAs(failure);
        if (severity == CanExceptionSeverity.Fault)
        {
            Func<Task> receive = async () => { await receiving; };
            (await receive.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
            allocator.RentCount.Should().Be(1, "the remaining native batch must not be materialized after cancellation");
            faults.Should().Be(1);
        }
        else
        {
            var received = await receiving;
            try
            {
                received.Select(d => d.CanFrame.ID).Should().Equal(Enumerable.Range(0x101, 63));
                allocator.DisposeCount.Should().Be(1, "the failed frame is discarded before reporting the error");
                faults.Should().Be(0, "the default callback severity must allow receiving to continue");
            }
            finally
            {
                foreach (var frame in received) frame.CanFrame.Dispose();
            }
        }
        allocator.DisposeCount.Should().Be(allocator.RentCount);
    }

    private sealed class PredicateRule(Func<CanFrame, bool> predicate) : IFilterRule
    {
        public CanFilterIDType FilterIdType => CanFilterIDType.Standard;
        public Func<CanFrame, bool> Build() => predicate;
    }

    [Fact]
    public void Native_Batches_Allocate_Frame_Buffers_Only_When_Enumerated()
    {
        var allocator = new CountingBufferAllocator();
        using var rx = (ControlCanBus)CanBus.Open(MaskRx, cfg => Configure(cfg).BufferAllocator(allocator));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        typeof(ControlCanBus).GetMethod("StopReceiveLoop", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(rx, null).Should().BeNull();
        var prepared = ControlCanEndpoint.Prepare(CanEndpoint.Parse(MaskRx), cfg => Configure(cfg));
        var device = (ControlCanDevice)typeof(ControlCanBus).GetField("_device", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(rx)!;
        var transceiver = prepared.Provider.Factory.CreateTransceivers(device.Options, prepared.BusConfigurator);
        tx.Transmit(Enumerable.Range(0, 64).Select(i => CanFrame.Classic(0x100 + i, new byte[] { 1 })).ToArray())
            .Should().Be(64);
        using (var frames = transceiver.Receive(rx, 64).GetEnumerator())
        {
            allocator.RentCount.Should().Be(0);
            frames.MoveNext().Should().BeTrue();
            allocator.RentCount.Should().Be(1);
            frames.Current.CanFrame.Dispose();
        }
        allocator.RentCount.Should().Be(1, "disposing an iterator does not materialize the remaining native structures");
        allocator.DisposeCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Sja1000_Rtr_Constraints_Work_With_One_Or_Duplicate_Rules(bool extended, bool remote)
    {
        var idType = extended ? CanFilterIDType.Extend : CanFilterIDType.Standard;
        var code = extended ? 0x180U : 0x24600000U;
        if (remote) code |= extended ? 4U : 0x00100000U;
        var mask = extended ? 3U : 0x000FFFFFU;
        var id = extended ? 0x30 : 0x123;
        for (var count = 1; count <= 2; count++)
        {
            using var rx = CanBus.Open(MaskRx, cfg =>
            {
                Configure(cfg).SoftwareFeaturesFallBack(CanFeature.MaskFilter);
                for (var i = 0; i < count; i++) cfg.AccMask(code, mask, idType);
            });
            using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
            if (count == 1) InvokeNative("AcceptanceMode", 4U, 2U, 0U).Should().Be((byte)1);
            tx.Transmit(new[]
            {
                CanFrame.Classic(id, new byte[] { 1 }, extended),
                CanFrame.Classic(id, isExtendedFrame: extended, isRemoteFrame: true),
                CanFrame.Classic(id + 1, isExtendedFrame: extended, isRemoteFrame: remote)
            }).Should().Be(3);
            var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
            got.Select(d => (d.CanFrame.ID, d.CanFrame.IsRemoteFrame)).Should().Equal((id, remote));
        }
    }

    [Theory]
    [InlineData(0x2460ABCDU, 0x001F0000U, true)] // RTR ignored; first two data bytes compared
    [InlineData(0x246AABCDU, 0U, false)] // unused register bits ignored even when AccMask is zero
    public async Task Sja1000_Data_Constraints_Respect_Missing_Bytes(uint code, uint mask, bool acceptsRemote)
    {
        for (var count = 1; count <= 2; count++)
        {
            using var rx = CanBus.Open(MaskRx, cfg =>
            {
                Configure(cfg).SoftwareFeaturesFallBack(CanFeature.MaskFilter);
                for (var i = 0; i < count; i++) cfg.AccMask(code, mask, CanFilterIDType.Standard);
            });
            using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
            tx.Transmit(new[]
            {
                CanFrame.Classic(0x123),
                CanFrame.Classic(0x123, new byte[] { 0xAB }),
                CanFrame.Classic(0x123, new byte[] { 0xAB, 0xCD }),
                CanFrame.Classic(0x123, new byte[] { 0xAA, 0xCD }),
                CanFrame.Classic(0x123, new byte[] { 0xAB, 0xCE }),
                CanFrame.Classic(0x123, isRemoteFrame: true),
                CanFrame.Classic(0x124)
            }).Should().Be(7);
            var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
            got.Select(d => (d.CanFrame.IsRemoteFrame, BitConverter.ToString(d.CanFrame.Data.ToArray())))
                .Should().Equal(acceptsRemote
                    ? new[] { (false, ""), (false, "AB"), (false, "AB-CD"), (true, "") }
                    : new[] { (false, ""), (false, "AB"), (false, "AB-CD") });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sja1000_Hardware_Register_Collisions_Still_Respect_The_Requested_Id_Type(bool extended)
    {
        // These two different frame formats produce the same 32-bit single-filter code.
        var allocator = new CountingBufferAllocator();
        using var rx = CanBus.Open(MaskRx, cfg => Configure(cfg).BufferAllocator(allocator)
            .AccMask(0x24600000U, extended ? 7U : 0x001FFFFFU,
                extended ? CanFilterIDType.Extend : CanFilterIDType.Standard));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        tx.Transmit(new[]
        {
            CanFrame.Classic(0x123, new byte[] { 0, 0 }),
            CanFrame.Classic(0x048C0000, new byte[] { 0, 0 }, isExtendedFrame: true)
        }).Should().Be(2);
        var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        try
        {
            got.Select(d => (d.CanFrame.ID, d.CanFrame.IsExtendedFrame))
                .Should().Equal((extended ? 0x048C0000 : 0x123, extended));
            allocator.RentCount.Should().Be(2, "native single filtering cannot distinguish these ID formats");
            allocator.DisposeCount.Should().Be(1);
        }
        finally
        {
            foreach (var frame in got) frame.CanFrame.Dispose();
        }
        allocator.DisposeCount.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_Defers_Native_Cleanup_Until_The_Active_Callback_Returns(bool disposeFromCallback)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var disposing = new ManualResetEventSlim();
        var allocator = new CountingBufferAllocator();
        using var rx = (ControlCanBus)CanBus.Open(RangeRx, cfg => Configure(cfg).BufferAllocator(allocator)
            .RangeFilter(0x100, 0x10F, CanFilterIDType.Standard));
        var ownerField = typeof(ControlCanBus).GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var owner = new TrackingOwner((IDisposable)ownerField.GetValue(rx)!);
        rx.AttachOwner(owner);
        using var tx = CanBus.Open(RangeTx, cfg => Configure(cfg));
        var observed = 0;
        rx.FrameObserved += (_, _) => Interlocked.Increment(ref observed);
#pragma warning disable CS0618 // Exercise shutdown inside the legacy owning-frame callback.
        rx.FrameReceived += (_, _) =>
        {
            if (disposeFromCallback) rx.Dispose();
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        };
#pragma warning restore CS0618
        Task? disposeTask = null;
        try
        {
            tx.Transmit(CanFrame.Classic(0x105, new byte[] { 1 })).Should().Be(1);
            entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
            var receiveCalls = InvokeNative("ReceiveCount", 21U, 2U, 0U);
            if (!disposeFromCallback)
            {
                disposeTask = Task.Run(() => { disposing.Set(); rx.Dispose(); }, TestContext.Current.CancellationToken);
                disposing.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
                // The bounded wait may end, but native resources must stay alive.
                (await Task.WhenAny(disposeTask, Task.Delay(5000, TestContext.Current.CancellationToken)))
                    .Should().BeSameAs(disposeTask);
            }
            InvokeNative("ResetCount", 21U, 2U, 0U).Should().Be(1);
            InvokeNative("ActiveRangeCount", 21U, 2U, 0U).Should().Be(1);
            InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(true);
            owner.DisposeCount.Should().Be(0, "the device lease must outlive the active receive task");
            Action reopen = () => { using var duplicate = CanBus.Open(RangeRx, cfg => Configure(cfg)); };
            reopen.Should().Throw<CanBusCreationException>().WithMessage("*already open or still stopping*");

            release.Set();
            if (disposeTask != null) await disposeTask;
            var pollTask = typeof(ControlCanBus).GetField("_pollTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
            SpinWait.SpinUntil(() => pollTask.GetValue(rx) == null, TimeSpan.FromSeconds(5)).Should().BeTrue();
            InvokeNative("ResetCount", 21U, 2U, 0U).Should().Be(2);
            InvokeNative("ActiveRangeCount", 21U, 2U, 0U).Should().Be(0);
            InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(false);
            InvokeNative("IsStarted", 21U, 2U, 1U).Should().Be(true);
            InvokeNative("ReceiveCount", 21U, 2U, 0U).Should().Be(receiveCalls,
                "cancellation must prevent the iterator from issuing another native receive");
            owner.DisposeCount.Should().Be(1);
            observed.Should().Be(0, "shutdown must not dispatch another callback for the current frame");
            allocator.DisposeCount.Should().Be(allocator.RentCount);
            using var reopened = CanBus.Open(RangeRx, cfg => Configure(cfg));
        }
        finally
        {
            release.Set();
            if (disposeTask != null) await disposeTask;
        }
    }

    private sealed class TrackingOwner(IDisposable inner) : IDisposable
    {
        private int _disposeCount;
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            inner.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Range_Cleanup_Failures_Are_Logged_Without_Hiding_The_Original_Error(bool throwingLogger)
    {
        using var sibling = CanBus.Open(RangeTx, cfg => Configure(cfg));
        var entries = new ConcurrentQueue<CanKitLogEntry>();
        var handlerField = typeof(CanKitLogger).GetField("_logHandler", BindingFlags.Static | BindingFlags.NonPublic)!;
        var originalHandler = (Action<CanKitLogEntry>)handlerField.GetValue(null)!;
        var openingThread = Thread.CurrentThread.ManagedThreadId;
        CanKitLogger.Configure(entry =>
        {
            if (Thread.CurrentThread.ManagedThreadId == openingThread && entry.Level == CanKitLogLevel.Warning
                && entry.Message.StartsWith("ControlCAN: Failed to clear range filters"))
            {
                entries.Enqueue(entry);
                if (throwingLogger) throw new InvalidOperationException("Injected logging failure");
            }
            originalHandler(entry);
        });
        InvokeNative("FailRangeWriteAfter", 21U, 2U, 0U, 1);
        InvokeNative("RejectReference", 21U, 2U, 0U, 3U);
        try
        {
            Action open = () =>
            {
                using var rx = CanBus.Open(RangeRx, cfg => Configure(cfg)
                    .RangeFilter(0x100, 0x10F, CanFilterIDType.Standard)
                    .RangeFilter(0x200, 0x20F, CanFilterIDType.Standard));
            };
            open.Should().Throw<ControlCanException>().WithMessage("*VCI_SetReference(Filter)*");
            entries.Should().NotBeEmpty();
            entries.Should().OnlyContain(e => e.Message.Contains("index=2") && e.Message.Contains("channel=0")
                && e.Exception is ControlCanException);
            InvokeNative("ResetCount", 21U, 2U, 0U).Should().Be(2);
        }
        finally
        {
            CanKitLogger.Configure(originalHandler);
            InvokeNative("AllowReference", 21U, 2U, 0U, 3U);
            using var cleanup = CanBus.Open(RangeRx, cfg => Configure(cfg));
        }
    }

    [Theory]
    [InlineData("VCI_USBCAN_E_U")]
    [InlineData("VCI_USBCAN_2E_U")]
    [InlineData("VCI_USBCAN_4E_U")]
    [InlineData("VCI_USBCAN_8E_U")]
    public async Task Range_Only_Providers_Keep_Mask_Capability_In_Software(string model)
    {
        var endpoint = $"controlcan://{model}?index=2#ch0";
        var capabilities = CanBus.QueryCapabilities(endpoint, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.MaskFilter));
        (capabilities.Features & CanFeature.MaskFilter).Should().Be((CanFeature)0);
        using var rx = CanBus.Open(endpoint, cfg => Configure(cfg).SoftwareFeaturesFallBack(CanFeature.MaskFilter)
            .AccMask(0x123, 0x7FF, CanFilterIDType.Standard));
        using var tx = CanBus.Open($"controlcan://{model}?index=2#ch1", cfg => Configure(cfg));
        tx.Transmit(new[]
        {
            CanFrame.Classic(0x123, new byte[] { 1 }),
            CanFrame.Classic(0x124, new byte[] { 2 }),
            CanFrame.Classic(0x123, new byte[] { 3 }, isExtendedFrame: true)
        }).Should().Be(3);
        (await rx.ReceiveAsync(8, 500, TestContext.Current.CancellationToken))
            .Select(d => d.CanFrame.ID).Should().Equal(0x123);
    }

    [Theory]
    [InlineData("VCI_USBCAN_2E_U", 21U)]
    [InlineData("VCI_USBCAN_4E_U", 31U)]
    public void Poll_Loop_Startup_Failure_Stops_Channel_And_Clears_Ranges(string model, uint deviceType)
    {
        using var sibling = CanBus.Open($"controlcan://{model}?index=2#ch1", cfg => Configure(cfg));
        var handlerField = typeof(CanKitLogger).GetField("_logHandler", BindingFlags.Static | BindingFlags.NonPublic)!;
        var originalHandler = (Action<CanKitLogEntry>)handlerField.GetValue(null)!;
        var openingThread = Thread.CurrentThread.ManagedThreadId;
        var failure = new InvalidOperationException("Injected poll startup failure");
        CanKitLogger.Configure(entry =>
        {
            if (Thread.CurrentThread.ManagedThreadId == openingThread && entry.Message == "ControlCAN: Starting poll loop.")
                throw failure;
            originalHandler(entry);
        });
        try
        {
            Action open = () =>
            {
                using var rx = CanBus.Open($"controlcan://{model}?index=2#ch0", cfg => Configure(cfg)
                    .RangeFilter(0x100, 0x10F, CanFilterIDType.Standard));
            };
            open.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
            InvokeNative("IsStarted", deviceType, 2U, 0U).Should().Be(false);
            InvokeNative("ActiveRangeCount", deviceType, 2U, 0U).Should().Be(0);
            InvokeNative("PendingRangeCount", deviceType, 2U, 0U).Should().Be(0);
            InvokeNative("IsStarted", deviceType, 2U, 1U).Should().Be(true,
                "failed channel creation must not stop a sibling channel");
        }
        finally
        {
            CanKitLogger.Configure(originalHandler);
        }
    }

    [Theory]
    [InlineData("VCI_USBCAN_2E_U", 21U)]
    [InlineData("VCI_USBCAN_4E_U", 31U)]
    public void Disposing_A_Channel_Stops_It_And_Clears_Its_Ranges(string model, uint deviceType)
    {
        using var sibling = CanBus.Open($"controlcan://{model}?index=2#ch1", cfg => Configure(cfg));
        using (CanBus.Open($"controlcan://{model}?index=2#ch0", cfg => Configure(cfg)
                   .RangeFilter(0x100, 0x10F, CanFilterIDType.Standard))) { }
        InvokeNative("IsStarted", deviceType, 2U, 0U).Should().Be(false);
        InvokeNative("ActiveRangeCount", deviceType, 2U, 0U).Should().Be(0);
        InvokeNative("IsStarted", deviceType, 2U, 1U).Should().Be(true);
    }

    [Theory]
    [InlineData("VCI_USBCAN_2E_U", 21U, 3U)]
    [InlineData("VCI_USBCAN_4E_U", 31U, 10U)]
    public async Task Failed_Range_Cleanup_Is_Remembered_Across_Device_Instances(string model, uint deviceType, uint clearReference)
    {
        var endpoint = $"controlcan://{model}?index=2#ch0";
        InvokeNative("PreserveRangeTablesOnClose", deviceType, 2U, true);
        InvokeNative("FailRangeWriteAfter", deviceType, 2U, 0U, 1);
        InvokeNative("RejectReference", deviceType, 2U, 0U, clearReference);
        try
        {
            Action firstOpen = () =>
            {
                using var rx = CanBus.Open(endpoint, cfg => Configure(cfg)
                    .RangeFilter(0x100, 0x10F, CanFilterIDType.Standard)
                    .RangeFilter(0x200, 0x20F, CanFilterIDType.Standard));
            };
            firstOpen.Should().Throw<ControlCanException>();
            InvokeNative("PendingRangeCount", deviceType, 2U, 0U).Should().Be(1);

            // The failed open released the last lease: this creates a different device object.
            Action secondOpen = () => { using var rx = CanBus.Open(endpoint, cfg => Configure(cfg)); };
            secondOpen.Should().Throw<ControlCanException>("the dirty native table survives the managed device instance");
            InvokeNative("OpenCount", deviceType, 2U).Should().Be(2);

            InvokeNative("AllowReference", deviceType, 2U, 0U, clearReference);
            using var recovered = CanBus.Open(endpoint, cfg => Configure(cfg)
                .RangeFilter(0x400, 0x40F, CanFilterIDType.Standard));
            using var tx = CanBus.Open($"controlcan://{model}?index=2#ch1", cfg => Configure(cfg));
            tx.Transmit(new[]
            {
                CanFrame.Classic(0x105, new byte[] { 1 }),
                CanFrame.Classic(0x405, new byte[] { 2 })
            }).Should().Be(2);
            (await recovered.ReceiveAsync(8, 1000, TestContext.Current.CancellationToken))
                .Select(d => d.CanFrame.ID).Should().Equal(0x405);
        }
        finally
        {
            InvokeNative("AllowReference", deviceType, 2U, 0U, clearReference);
            InvokeNative("PreserveRangeTablesOnClose", deviceType, 2U, false);
            using var cleanup = CanBus.Open(endpoint, cfg => Configure(cfg));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_Filter_Reconfiguration_Is_Rejected_Without_Changing_Active_Filters(bool software)
    {
        using var rx = (ControlCanBus)CanBus.Open(RangeRx, cfg =>
        {
            Configure(cfg).SoftwareFeaturesFallBack(CanFeature.MaskFilter);
            if (software) cfg.AccMask(0x100, 0x7F0, CanFilterIDType.Standard);
            else cfg.RangeFilter(0x100, 0x10F, CanFilterIDType.Standard);
        });
        using var tx = CanBus.Open(RangeTx, cfg => Configure(cfg));
        var replacement = ControlCanEndpoint.Prepare(CanEndpoint.Parse(RangeRx), cfg => Configure(cfg)
            .RangeFilter(0x400, 0x40F, CanFilterIDType.Standard));
        Action reconfigure = () => rx.ApplyConfigAfterInit(replacement.BusOptions);
        reconfigure.Should().Throw<CanBusConfigurationException>().WithMessage("*reopen*");
        tx.Transmit(new[]
        {
            CanFrame.Classic(0x105, new byte[] { 1 }),
            CanFrame.Classic(0x405, new byte[] { 2 })
        }).Should().Be(2);
        (await rx.ReceiveAsync(8, 1000, TestContext.Current.CancellationToken))
            .Select(d => d.CanFrame.ID).Should().Equal(0x105);
    }

    [Fact]
    public async Task ClearBuffer_Does_Not_Stop_The_Native_Channel()
    {
        using var rx = CanBus.Open(MaskRx, cfg => Configure(cfg));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        rx.ClearBuffer();
        tx.Transmit(CanFrame.Classic(0x123, new byte[] { 1 })).Should().Be(1);
        (await rx.ReceiveAsync(1, 1000, TestContext.Current.CancellationToken))
            .Select(d => d.CanFrame.ID).Should().Equal(0x123);
    }

    [Fact]
    public async Task Explicit_Software_Only_Filter_Is_Evaluated_Once_Per_Frame()
    {
        var rule = new CountingFilterRule();
        var filter = new CanFilter();
        filter.SoftwareFilterRules.Add(rule);
        using var rx = CanBus.Open(MaskRx, cfg => Configure(cfg).SetFilter(filter));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        tx.Transmit(new[]
        {
            CanFrame.Classic(0x105, new byte[] { 1 }),
            CanFrame.Classic(0x405, new byte[] { 2 })
        }).Should().Be(2);
        (await rx.ReceiveAsync(8, 1000, TestContext.Current.CancellationToken))
            .Select(d => d.CanFrame.ID).Should().Equal(0x105);
        rule.Evaluations.Should().Be(2);
        filter.FilterRules.Should().BeEmpty();
    }

    private sealed class CountingFilterRule : IFilterRule
    {
        private int _evaluations;
        public int Evaluations => Volatile.Read(ref _evaluations);
        public CanFilterIDType FilterIdType => CanFilterIDType.Standard;
        public Func<CanFrame, bool> Build() => frame =>
        {
            Interlocked.Increment(ref _evaluations);
            return frame.ID == 0x105;
        };
    }

    [Theory]
    [InlineData("VCI_USBCAN_2E_U", 21U, 0)]
    [InlineData("VCI_USBCAN_2E_U", 21U, 1)]
    [InlineData("VCI_USBCAN_2E_U", 21U, 2)]
    [InlineData("VCI_USBCAN_4E_U", 31U, 0)]
    [InlineData("VCI_USBCAN_4E_U", 31U, 1)]
    [InlineData("VCI_USBCAN_4E_U", 31U, 2)]
    public async Task Fresh_Channel_Does_Not_Require_Filter_Clear_Commands(string model, uint deviceType, int mode)
    {
        using var tx = CanBus.Open($"controlcan://{model}?index=2#ch1", cfg => Configure(cfg));
        foreach (var reference in new[] { 3U, 9U, 10U })
            InvokeNative("RejectReference", deviceType, 2U, 0U, reference);

        var endpoint = $"controlcan://{model}?index=2#ch0";
        try
        {
            using var rx = CanBus.Open(endpoint, cfg =>
            {
                Configure(cfg).SoftwareFeaturesFallBack(CanFeature.MaskFilter);
                if (mode == 1) cfg.AccMask(0x400, 0x7F0, CanFilterIDType.Standard);
                if (mode == 2) cfg.RangeFilter(0x400, 0x40F, CanFilterIDType.Standard);
            });
            tx.Transmit(CanFrame.Classic(0x405, new byte[] { 1 })).Should().Be(1);
            (await rx.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken))
                .Select(d => d.CanFrame.ID).Should().Equal(0x405);
        }
        finally
        {
            foreach (var reference in new[] { 3U, 9U, 10U })
                InvokeNative("AllowReference", deviceType, 2U, 0U, reference);
            using var cleanup = CanBus.Open(endpoint, cfg => Configure(cfg));
        }
    }

    [Theory]
    [InlineData("VCI_USBCAN_2E_U", 21U, 3U)]
    [InlineData("VCI_USBCAN_4E_U", 31U, 10U)]
    public void Stale_Hardware_Filters_Must_Be_Cleared_Before_Reopening(string model, uint deviceType, uint reference)
    {
        var endpoint = $"controlcan://{model}?index=2#ch0";
        using var tx = CanBus.Open($"controlcan://{model}?index=2#ch1", cfg => Configure(cfg));
        try
        {
            using (CanBus.Open(endpoint, cfg => Configure(cfg).RangeFilter(0x100, 0x10F, CanFilterIDType.Standard)))
                InvokeNative("RejectReference", deviceType, 2U, 0U, reference);
            Action reopen = () => { using var rx = CanBus.Open(endpoint, cfg => Configure(cfg)); };
            reopen.Should().Throw<ControlCanException>("an old hardware filter must not silently restrict an unfiltered channel");
        }
        finally
        {
            InvokeNative("AllowReference", deviceType, 2U, 0U, reference);
            using var cleanup = CanBus.Open(endpoint, cfg => Configure(cfg));
        }
    }

    [Fact]
    public async Task Eight_E_U_Ranges_Use_Software_Without_Unverified_Reference_Commands()
    {
        using var tx = CanBus.Open("controlcan://VCI_USBCAN_8E_U?index=2#ch1", cfg => Configure(cfg));
        foreach (var reference in new[] { 1U, 2U, 3U, 7U, 8U, 9U, 10U })
            InvokeNative("RejectReference", 34U, 2U, 0U, reference);
        foreach (var first in new[] { 0x100, 0x400 })
        {
            using var rx = CanBus.Open("controlcan://VCI_USBCAN_8E_U?index=2#ch0", cfg => Configure(cfg).SoftwareFeaturesFallBack(CanFeature.RangeFilter)
                .RangeFilter(first, first + 0xF, CanFilterIDType.Standard));
            tx.Transmit(new[]
            {
                CanFrame.Classic(0x105, new byte[] { 1 }),
                CanFrame.Classic(0x405, new byte[] { 2 }),
            }).Should().Be(2);
            (await rx.ReceiveAsync(8, 1000, TestContext.Current.CancellationToken))
                .Select(d => d.CanFrame.ID).Should().Equal(first + 5);
        }
    }

    [Fact]
    public async Task Batch_Limit_Does_Not_Delay_Matching_Frames_Behind_A_Large_Backlog()
    {
        using var rx = (ControlCanBus)CanBus.Open(MaskRx, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.MaskFilter)
            .AccMask(0x24600000U, 0x001FFFFFU, CanFilterIDType.Standard)
            .AccMask(0x24600000U, 0x001FFFFFU, CanFilterIDType.Standard));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        var burst = Enumerable.Range(0, 4500)
            .Select(_ => CanFrame.Classic(0x300, new byte[] { 1 }))
            .Append(CanFrame.Classic(0x123, new byte[] { 2 })).ToArray();
        rx.FrameObserved += (_, data) =>
        {
            if (data.CanFrame.Data.Span[0] != 0) return;
            rx.Options.PollingInterval = 10_000;
            tx.Transmit(burst);
        };
        tx.Transmit(CanFrame.Classic(0x123, new byte[] { 0 })).Should().Be(1);
        var got = await rx.ReceiveAsync(2, 2000, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.Data.Span[0]).Should().Equal(0, 2);
    }

    [Theory]
    [InlineData("VCI_USBCAN_2E_U")]
    [InlineData("VCI_USBCAN_4E_U")]
    public async Task Reopening_With_Software_Fallback_Removes_Previous_Hardware_Ranges(string model)
    {
        var rxEndpoint = $"controlcan://{model}?index=2#ch0";
        using var tx = CanBus.Open($"controlcan://{model}?index=2#ch1", cfg => Configure(cfg));
        using (var previous = CanBus.Open(rxEndpoint, cfg => Configure(cfg)
                   .RangeFilter(0x100, 0x10F, CanFilterIDType.Standard)))
        {
            tx.Transmit(CanFrame.Classic(0x105, new byte[] { 1 })).Should().Be(1);
            (await previous.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken)).Should().HaveCount(1);
        }

        using var rx = CanBus.Open(rxEndpoint, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.MaskFilter)
            .AccMask(0x400, 0x7F0, CanFilterIDType.Standard));
        tx.Transmit(new[]
        {
            CanFrame.Classic(0x105, new byte[] { 1 }),
            CanFrame.Classic(0x405, new byte[] { 2 }),
        }).Should().Be(2);
        var got = await rx.ReceiveAsync(8, 1000, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.ID).Should().Equal(0x405);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Invalid_Sja1000_Id_Types_Are_Rejected_Before_Initialization(int count)
    {
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        Action open = () =>
        {
            using var rx = CanBus.Open(MaskRx, cfg =>
            {
                Configure(cfg).SoftwareFeaturesFallBack(CanFeature.MaskFilter);
                for (var i = 0; i < count; i++) cfg.AccMask(0U, uint.MaxValue, (CanFilterIDType)42);
            });
        };
        open.Should().Throw<CanFilterConfigurationException>();
        InvokeNative("InitializationCount", 4U, 2U, 0U).Should().Be(0,
            "unsupported filters must be rejected before initializing the native channel");
    }

    [Theory]
    [InlineData("VCI_USBCAN_2E_U", 21U)]
    [InlineData("VCI_USBCAN_4E_U", 31U)]
    public async Task Failed_Range_Programming_Clears_Pending_Rules_Before_Reopening(string model, uint deviceType)
    {
        var rxEndpoint = $"controlcan://{model}?index=2#ch0";
        var txEndpoint = $"controlcan://{model}?index=2#ch1";
        using var tx = CanBus.Open(txEndpoint, cfg => Configure(cfg));
        var api = typeof(ControlCanBus).Assembly.GetType("CanKit.Adapter.ControlCAN.Native.ControlCAN", true)!;
        api.GetMethod("FailRangeWriteAfter", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { deviceType, 2U, 0U, 2 });

        Action open = () =>
        {
            using var failed = CanBus.Open(rxEndpoint, cfg => Configure(cfg)
                .RangeFilter(0x100, 0x10F, CanFilterIDType.Standard)
                .RangeFilter(0x200, 0x20F, CanFilterIDType.Standard)
                .RangeFilter(0x300, 0x30F, CanFilterIDType.Standard));
        };
        open.Should().Throw<ControlCanException>();
        InvokeNative("ResetCount", deviceType, 2U, 0U).Should().Be(2,
            "reset precedes programming, and failure cleanup resets the channel again");
        api.GetMethod("PendingRangeCount", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { deviceType, 2U, 0U }).Should().Be(0);

        // Keep the sibling channel open: closing the whole device cannot hide stale records.
        using var rx = CanBus.Open(rxEndpoint, cfg => Configure(cfg).RangeFilter(0x400, 0x40F, CanFilterIDType.Standard));
        tx.Transmit(new[]
        {
            CanFrame.Classic(0x105, new byte[] { 1 }),
            CanFrame.Classic(0x205, new byte[] { 2 }),
            CanFrame.Classic(0x405, new byte[] { 3 }),
        }).Should().Be(3);
        var got = await rx.ReceiveAsync(8, 1000, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.ID).Should().Equal(0x405);
    }

    [Fact]
    public async Task Sustained_Receive_Traffic_Does_Not_Starve_Error_Reporting()
    {
        using var rx = CanBus.Open(MaskRx, cfg => Configure(cfg)
            .EnableErrorInfo().SetAsyncBufferCapacity(1));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        var api = typeof(ControlCanBus).Assembly.GetType("CanKit.Adapter.ControlCAN.Native.ControlCAN", true)!;
        var injectError = api.GetMethod("InjectError", BindingFlags.Static | BindingFlags.NonPublic)!;
        var errorSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var keepSending = 1;
        var received = 0;
        var frame = CanFrame.Classic(0x123, new byte[] { 1 });
        rx.FrameObserved += (_, _) =>
        {
            if (Interlocked.Increment(ref received) == 1)
                injectError.Invoke(null, new object[] { 4U, 2U, 0U, 0x10U });
            // Refill the native queue before this batch ends, so it never becomes empty.
            if (Volatile.Read(ref keepSending) != 0)
                tx.Transmit(frame);
        };
        rx.ErrorFrameReceived += (_, _) =>
        {
            errorSeen.TrySetResult(Volatile.Read(ref keepSending) != 0);
            Volatile.Write(ref keepSending, 0);
        };

        try
        {
            tx.Transmit(frame).Should().Be(1);
            var finished = await Task.WhenAny(errorSeen.Task,
                Task.Delay(5000, TestContext.Current.CancellationToken));
            finished.Should().BeSameAs(errorSeen.Task, "error reporting must run while traffic continues");
            (await errorSeen.Task).Should().BeTrue();
        }
        finally
        {
            Volatile.Write(ref keepSending, 0);
        }
    }

    [Fact]
    public async Task Reusing_A_Filter_Does_Not_Retain_Previous_Fallback_Rules()
    {
        var filter = new CanFilter();
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        foreach (var id in new[] { 0x123, 0x456 })
        {
            filter.FilterRules.Clear();
            filter.FilterRules.Add(new FilterRule.Mask((uint)id << 21, 0x001FFFFF));
            filter.FilterRules.Add(new FilterRule.Mask((uint)id << 21, 0x001FFFFF));
            using var rx = CanBus.Open(MaskRx, cfg => Configure(cfg)
                .SoftwareFeaturesFallBack(CanFeature.MaskFilter).SetFilter(filter));
            tx.Transmit(new[]
            {
                CanFrame.Classic(0x123, new byte[] { 1 }),
                CanFrame.Classic(0x456, new byte[] { 2 }),
            }).Should().Be(2);

            var got = await rx.ReceiveAsync(8, 1000, TestContext.Current.CancellationToken);
            got.Select(d => d.CanFrame.ID).Should().Equal(id);
        }
        filter.SoftwareFilterRules.Should().BeEmpty("derived fallback rules belong to the bus, not the caller's options");
    }

    [Theory]
    [InlineData(0)] // no hardware rule
    [InlineData(1)] // hardware mask
    [InlineData(2)] // hardware range
    [InlineData(3)] // masks in software
    [InlineData(4)] // mixed ranges/masks in software
    public async Task Explicit_Software_Rules_Restrict_The_Configured_Filter(int mode)
    {
        var filter = new CanFilter();
        if (mode is 1 or 3)
            filter.FilterRules.Add(new FilterRule.Mask(0x20000000, 0x01FFFFFF));
        if (mode == 3)
            filter.FilterRules.Add(new FilterRule.Mask(0x20000000, 0x01FFFFFF));
        if (mode is 2 or 4)
            filter.FilterRules.Add(new FilterRule.Range(0x100, 0x10F));
        if (mode == 4)
            filter.FilterRules.Add(new FilterRule.Mask(0x200, 0x7F0));
        filter.SoftwareFilterRules.Add(new FilterRule.Mask(0x105, 0x7FF));
        filter.SoftwareFilterRules.Add(new FilterRule.Mask(0x405, 0x7FF));
        var originalSoftwareRules = filter.SoftwareFilterRules.ToArray();

        using var rx = CanBus.Open(mode is 2 or 4 ? RangeRx : MaskRx, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.Filters).SetFilter(filter));
        using var tx = CanBus.Open(mode is 2 or 4 ? RangeTx : MaskTx, cfg => Configure(cfg));
        tx.Transmit(new[]
        {
            CanFrame.Classic(0x105, new byte[] { 1 }), // both stages
            CanFrame.Classic(0x106, new byte[] { 2 }), // configured rules only
            CanFrame.Classic(0x405, new byte[] { 3 }), // explicit software rules only
            CanFrame.Classic(0x300, new byte[] { 4 }), // neither
        }).Should().Be(4);

        var got = await rx.ReceiveAsync(8, 1000, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.ID).Should().Equal(mode == 0 ? new[] { 0x105, 0x405 } : new[] { 0x105 });
        filter.SoftwareFilterRules.Should().Equal(originalSoftwareRules);
    }

    [Theory]
    [InlineData(false, 0x24600000U, 0x001FFFFFU, 0x123, 0x123, 0x122)]
    [InlineData(false, 0x24600000U, 0x007FFFFFU, 0x120, 0x123, 0x124)]
    [InlineData(false, 0xFFE00000U, 0x001FFFFFU, 0x7FF, 0x7FF, 0x7FE)]
    [InlineData(true, 0x00000180U, 0x00000007U, 0x030, 0x030, 0x038)]
    [InlineData(true, 0xFFFFFFF8U, 0x00000007U, 0x1FFFFFFF, 0x1FFFFFFF, 0x1FFFFFFE)]
    [InlineData(true, 0xFFFFFFF8U, 0x0000001FU, 0x1FFFFFFC, 0x1FFFFFFF, 0x1FFFFFFB)]
    public async Task Sja1000_Masks_Match_The_Same_Ids_In_Hardware_And_Software(
        bool extended, uint accCode, uint accMask, int firstMatch, int secondMatch, int rejected)
    {
        var idType = extended ? CanFilterIDType.Extend : CanFilterIDType.Standard;
        // One rule uses hardware; repeating the identical rule forces the software path.
        for (var ruleCount = 1; ruleCount <= 2; ruleCount++)
        {
            using var rx = CanBus.Open(MaskRx, cfg =>
            {
                Configure(cfg).SoftwareFeaturesFallBack(CanFeature.MaskFilter);
                for (var i = 0; i < ruleCount; i++)
                    cfg.AccMask(accCode, accMask, idType);
            });
            using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));

            tx.Transmit(new[]
            {
                CanFrame.Classic(firstMatch, new byte[] { 1 }, isExtendedFrame: extended),
                CanFrame.Classic(rejected, new byte[] { 2 }, isExtendedFrame: extended),
                CanFrame.Classic(secondMatch, new byte[] { 3 }, isExtendedFrame: extended),
                CanFrame.Classic(firstMatch & 0x7FF, new byte[] { 4 }, isExtendedFrame: !extended),
            }).Should().Be(4);

            var got = await rx.ReceiveAsync(8, 1000, TestContext.Current.CancellationToken);
            got.Select(d => (d.CanFrame.ID, d.CanFrame.IsExtendedFrame))
                .Should().Equal((firstMatch, extended), (secondMatch, extended));
            rx.Options.Filter.SoftwareFilterRules.Should().BeEmpty();
            rx.Options.Filter.FilterRules.Should().OnlyContain(
                rule => rule.Equals(new FilterRule.Mask(accCode, accMask, idType)),
                "conversion must not mutate the original hardware rules");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sja1000_Ignore_All_Masks_Accept_All_Ids_Of_Their_Type(bool extended)
    {
        var idType = extended ? CanFilterIDType.Extend : CanFilterIDType.Standard;
        using var rx = CanBus.Open(MaskRx, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.MaskFilter)
            .AccMask(uint.MaxValue, uint.MaxValue, idType)
            .AccMask(uint.MaxValue, uint.MaxValue, idType));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        var maxId = extended ? 0x1FFFFFFF : 0x7FF;

        tx.Transmit(new[]
        {
            CanFrame.Classic(0, new byte[] { 1 }, isExtendedFrame: extended),
            CanFrame.Classic(maxId, new byte[] { 2 }, isExtendedFrame: extended),
            CanFrame.Classic(0, new byte[] { 3 }, isExtendedFrame: !extended),
        }).Should().Be(3);

        var got = await rx.ReceiveAsync(8, 1000, TestContext.Current.CancellationToken);
        got.Select(d => (d.CanFrame.ID, d.CanFrame.IsExtendedFrame))
            .Should().Equal((0, extended), (maxId, extended));
    }

    [Theory]
    [InlineData(false, 0x400, 0x7F0)]
    [InlineData(true, 0x400, 0x1FFFFFF0)]
    [InlineData(true, 0x18DA0400, 0x1FFFFFF0)]
    public async Task Single_Mask_On_A_Range_Device_Uses_Software_Fallback(bool extended, int code, int mask)
    {
        var idType = extended ? CanFilterIDType.Extend : CanFilterIDType.Standard;
        using var rx = CanBus.Open(RangeRx, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.MaskFilter)
            .AccMask(code, mask, idType));
        using var tx = CanBus.Open(RangeTx, cfg => Configure(cfg));

        tx.Transmit(new[]
        {
            CanFrame.Classic(code, new byte[] { 1 }, isExtendedFrame: extended),
            CanFrame.Classic(code + 0x10, new byte[] { 2 }, isExtendedFrame: extended),
            CanFrame.Classic(code + 0xF, new byte[] { 3 }, isExtendedFrame: extended),
            CanFrame.Classic(code & 0x7FF, new byte[] { 4 }, isExtendedFrame: !extended),
        }).Should().Be(4);

        var got = await rx.ReceiveAsync(8, 1000, TestContext.Current.CancellationToken);
        got.Select(d => (d.CanFrame.ID, d.CanFrame.IsExtendedFrame))
            .Should().Equal((code, extended), (code + 0xF, extended));
        rx.Options.Filter.SoftwareFilterRules.Should().BeEmpty();
        rx.Options.Filter.FilterRules.Should()
            .ContainSingle().Which.Should().Be(new FilterRule.Mask((uint)code, (uint)mask, idType));
    }

    [Fact]
    public async Task Range_And_Mask_Rules_Are_Evaluated_In_Software_As_A_Union()
    {
        using var rx = CanBus.Open(RangeRx, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.MaskFilter)
            .RangeFilter(0x100, 0x1FF, CanFilterIDType.Standard)
            .AccMask(0x400, 0x7F0, CanFilterIDType.Standard));
        using var tx = CanBus.Open(RangeTx, cfg => Configure(cfg));

        rx.Options.Filter.SoftwareFilterRules.Should().BeEmpty("fallback must not mutate the caller's rules");

        tx.Transmit(new[]
        {
            CanFrame.Classic(0x100, new byte[] { 1 }), // range only
            CanFrame.Classic(0x300, new byte[] { 2 }), // neither
            CanFrame.Classic(0x405, new byte[] { 3 }), // mask only
            CanFrame.Classic(0x1FF, new byte[] { 4 }), // range only
            CanFrame.Classic(0x4FF, new byte[] { 5 }), // neither
        });

        var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.ID).Should().Equal(0x100, 0x405, 0x1FF);
    }

    [Fact]
    public async Task Range_Rules_Alone_Stay_In_Hardware()
    {
        using var rx = CanBus.Open(RangeRx, cfg => Configure(cfg)
            .RangeFilter(0x100, 0x1FF, CanFilterIDType.Standard)
            .RangeFilter(0x400, 0x40F, CanFilterIDType.Standard));
        using var tx = CanBus.Open(RangeTx, cfg => Configure(cfg));

        rx.Options.Filter.SoftwareFilterRules.Should().BeEmpty();

        tx.Transmit(new[]
        {
            CanFrame.Classic(0x100, new byte[] { 1 }),
            CanFrame.Classic(0x300, new byte[] { 2 }),
            CanFrame.Classic(0x405, new byte[] { 3 }),
        });

        var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.ID).Should().Equal(0x100, 0x405);
    }

    [Fact]
    public async Task Several_Mask_Rules_On_A_Mask_Device_Are_Evaluated_In_Software_As_A_Union()
    {
        using var rx = CanBus.Open(MaskRx, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.MaskFilter)
            .AccMask(0x20000000U, 0x01FFFFFFU, CanFilterIDType.Standard)
            .AccMask(0x80000000U, 0x01FFFFFFU, CanFilterIDType.Standard));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));

        rx.Options.Filter.SoftwareFilterRules.Should().BeEmpty("fallback must not mutate the caller's rules");

        tx.Transmit(new[]
        {
            CanFrame.Classic(0x105, new byte[] { 1 }), // first mask
            CanFrame.Classic(0x205, new byte[] { 2 }), // neither
            CanFrame.Classic(0x405, new byte[] { 3 }), // second mask
        });

        var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.ID).Should().Equal(0x105, 0x405);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Matching_Frames_After_Fully_Rejected_Batches_Arrive_In_The_Same_Poll(bool mixedRules)
    {
        var allocator = new CountingBufferAllocator();
        using var rx = (ControlCanBus)CanBus.Open(mixedRules ? RangeRx : MaskRx, cfg =>
        {
            Configure(cfg).BufferAllocator(allocator).SoftwareFeaturesFallBack(CanFeature.MaskFilter);
            if (mixedRules)
                cfg.RangeFilter(0x100, 0x1FF, CanFilterIDType.Standard);
            else
                cfg.AccMask(0x20000000U, 0x01FFFFFFU, CanFilterIDType.Standard);
            cfg.AccMask(mixedRules ? 0x400U : 0x80000000U,
                mixedRules ? 0x7F0U : 0x01FFFFFFU, CanFilterIDType.Standard);
        });
        using var tx = CanBus.Open(mixedRules ? RangeTx : MaskTx, cfg => Configure(cfg));
        var burst = Enumerable.Range(0, 128)
            .Select(_ => CanFrame.Classic(0x300, new byte[] { 1 }))
            .Concat(new[]
            {
                CanFrame.Classic(0x105, new byte[] { 2 }),
                CanFrame.Classic(0x405, new byte[] { 3 }),
            }).ToArray();

        rx.FrameObserved += (_, data) =>
        {
            if (data.CanFrame.ID != 0x100) return;
            // Queue two fully rejected native batches while the current drain is active.
            // A second poll is deliberately too late to satisfy the receive below.
            rx.Options.PollingInterval = 10_000;
            tx.Transmit(burst);
        };
        tx.Transmit(CanFrame.Classic(0x100, new byte[] { 0 })).Should().Be(1);

        var got = await rx.ReceiveAsync(3, 1000, TestContext.Current.CancellationToken);
        try
        {
            got.Select(d => d.CanFrame.ID).Should().Equal(0x100, 0x105, 0x405);
            allocator.RentCount.Should().Be(131, "one poll must drain all received frames, including rejected batches");
            allocator.DisposeCount.Should().Be(128, "every rejected frame must release its buffer");
        }
        finally
        {
            foreach (var frame in got)
                frame.CanFrame.Dispose();
        }
        allocator.DisposeCount.Should().Be(allocator.RentCount);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Software_Filter_Releases_Rejected_Frames_And_Preserves_Accepted_Frames(
        bool mixedRules, bool includeAccepted)
    {
        var allocator = new CountingBufferAllocator();
        using var rx = CanBus.Open(mixedRules ? RangeRx : MaskRx, cfg =>
        {
            Configure(cfg).BufferAllocator(allocator).SoftwareFeaturesFallBack(CanFeature.MaskFilter);
            if (mixedRules)
                cfg.RangeFilter(0x100, 0x1FF, CanFilterIDType.Standard);
            else
                cfg.AccMask(0x20000000U, 0x01FFFFFFU, CanFilterIDType.Standard);
            cfg.AccMask(mixedRules ? 0x400U : 0x80000000U,
                mixedRules ? 0x7F0U : 0x01FFFFFFU, CanFilterIDType.Standard);
        });
        using var tx = CanBus.Open(mixedRules ? RangeTx : MaskTx, cfg => Configure(cfg));

        tx.Transmit(new[]
        {
            CanFrame.Classic(0x300, new byte[] { 1 }),
            CanFrame.Classic(0x4FF, new byte[] { 2 }),
            CanFrame.Classic(includeAccepted ? 0x105 : 0x305, new byte[] { 3 }),
            CanFrame.Classic(includeAccepted ? 0x405 : 0x505, new byte[] { 4 }),
        }).Should().Be(4);

        var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        try
        {
            allocator.RentCount.Should().Be(4, "every frame passes the open hardware filter");
            allocator.DisposeCount.Should().Be(includeAccepted ? 2 : 4,
                "only rejected frames should have been released before delivery");
            got.Select(d => d.CanFrame.ID).Should().Equal(includeAccepted ? new[] { 0x105, 0x405 } : Array.Empty<int>());
            if (includeAccepted)
                got.Select(d => d.CanFrame.Data.Span[0]).Should().Equal(3, 4);
        }
        finally
        {
            foreach (var frame in got)
                frame.CanFrame.Dispose();
        }

        allocator.DisposeCount.Should().Be(allocator.RentCount, "every rental must be released exactly once");
    }

    private sealed class CountingBufferAllocator : IBufferAllocator
    {
        private int _rentCount;
        private int _disposeCount;

        public int RentCount => Volatile.Read(ref _rentCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public bool FrameNeedDispose => true;

        public IMemoryOwner<byte> Rent(int length, bool zeroFill = false)
        {
            Interlocked.Increment(ref _rentCount);
            return new TrackedOwner(length, this);
        }

        private sealed class TrackedOwner(int length, CountingBufferAllocator allocator) : IMemoryOwner<byte>
        {
            private byte[]? _buffer = new byte[length];

            public Memory<byte> Memory => _buffer ?? throw new ObjectDisposedException(nameof(TrackedOwner));

            public void Dispose()
            {
                Interlocked.Increment(ref allocator._disposeCount);
                _buffer = null;
            }
        }
    }

    private static IBusInitOptionsConfigurator Configure(IBusInitOptionsConfigurator cfg)
        => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000);

    private static object? InvokeNative(string method, params object[] args)
        => typeof(ControlCanBus).Assembly.GetType("CanKit.Adapter.ControlCAN.Native.ControlCAN", true)!
            .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
}
#endif
