#if FAKE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Adapter.ControlCAN;
using CanKit.Adapter.ControlCAN.Definitions;
using CanKit.Adapter.ControlCAN.Options;
using CanKit.Core;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

// Keep the same test class so xUnit serializes access to the dedicated fake device indices.
public partial class ControlCanFilterRulesTests
{
    [Theory]
    [InlineData("VCI_USBCAN_2E_U", 21U, false)]
    [InlineData("VCI_USBCAN_2E_U", 21U, true)]
    [InlineData("VCI_USBCAN_4E_U", 31U, false)]
    [InlineData("VCI_USBCAN_4E_U", 31U, true)]
    public async Task Hardware_Ranges_Are_Programmed_After_Reset(string model, uint deviceType, bool resetClearsRanges)
    {
        using var tx = CanBus.Open($"controlcan://{model}?index=2#ch1", cfg => Configure(cfg));
        InvokeNative("ClearRangesOnReset", deviceType, 2U, 0U, resetClearsRanges);
        using var rx = CanBus.Open($"controlcan://{model}?index=2#ch0", cfg => Configure(cfg)
            .RangeFilter(0x100, 0x10F, CanFilterIDType.Standard));
        tx.Transmit(new[] { CanFrame.Classic(0x105), CanFrame.Classic(0x205) }).Should().Be(2);
        (await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken))
            .Select(frame => frame.CanFrame.ID).Should().Equal(0x105);
        InvokeNative("ActiveRangeCount", deviceType, 2U, 0U).Should().Be(1);
    }

    [Theory]
    [InlineData("VCI_USBCAN2", 4U, 0, "MaskFilter")]
    [InlineData("VCI_USBCAN_2E_U", 21U, 1, "MaskFilter")]
    [InlineData("VCI_USBCAN_2E_U", 21U, 2, "MaskFilter")]
    [InlineData("VCI_USBCAN_8E_U", 34U, 3, "RangeFilter")]
    [InlineData("VCI_USBCAN2", 4U, 3, "RangeFilter")]
    public void Filter_Fallback_Requires_Explicit_Unsupported_Features(string model, uint deviceType, int mode, string feature)
    {
        using var sibling = CanBus.Open($"controlcan://{model}?index=2#ch1", cfg => Configure(cfg));
        var filter = new CanFilter();
        if (mode == 0)
        {
            filter.FilterRules.Add(new FilterRule.Mask(0x24600000U, 0x001FFFFFU));
            filter.FilterRules.Add(new FilterRule.Mask(0x24600000U, 0x001FFFFFU));
        }
        if (mode is 1 or 2) filter.FilterRules.Add(new FilterRule.Mask(0x123, 0x7FF));
        if (mode is 2 or 3) filter.FilterRules.Add(new FilterRule.Range(0x400, 0x40F));
        Action open = () =>
        {
            // Exercise final-plan validation even when a previously enabled fallback is revoked.
            using var rx = CanBus.Open($"controlcan://{model}?index=2#ch0", cfg => Configure(cfg)
                .SoftwareFeaturesFallBack(CanFeature.Filters).SetFilter(filter)
                .SoftwareFeaturesFallBack((CanFeature)0));
        };
        open.Should().Throw<CanFilterConfigurationException>().WithMessage($"*{feature}*");
        InvokeNative("InitializationCount", deviceType, 2U, 0U).Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Mixed_Rules_Need_Fallback_Only_For_Unsupported_Rule_Types(bool rangeFirst, bool unsignedMask)
    {
        using var rx = CanBus.Open(MaskRx, cfg =>
        {
            Configure(cfg).SoftwareFeaturesFallBack(CanFeature.RangeFilter);
            if (rangeFirst) cfg.RangeFilter(0x400, 0x40F, CanFilterIDType.Standard);
            if (unsignedMask) cfg.AccMask(0x24600000U, 0x001FFFFFU, CanFilterIDType.Standard);
            else cfg.AccMask(0x24600000, 0x001FFFFF, CanFilterIDType.Standard);
            if (!rangeFirst) cfg.RangeFilter(0x400, 0x40F, CanFilterIDType.Standard);
        });
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        tx.Transmit(new[] { CanFrame.Classic(0x123), CanFrame.Classic(0x300), CanFrame.Classic(0x405) });
        (await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken))
            .Select(frame => frame.CanFrame.ID).Should().Equal(0x123, 0x405);
    }

    [Theory]
    [InlineData((CanFeature)0, false)]
    [InlineData((CanFeature)0, true)]
    [InlineData(CanFeature.MaskFilter, false)]
    [InlineData(CanFeature.MaskFilter, true)]
    [InlineData(CanFeature.RangeFilter, false)]
    [InlineData(CanFeature.RangeFilter, true)]
    [InlineData(CanFeature.Filters, false)]
    [InlineData(CanFeature.Filters, true)]
    public async Task Combined_Capabilities_Require_Fallback_For_A_Mixed_Filter_List(CanFeature fallback, bool rangeFirst)
    {
        // No built-in model advertises both features, but providers are extensible.
        var provider = new CombinedFilterProvider();
        var (deviceOptions, _) = provider.GetDeviceOptions();
        ((ControlCanDeviceOptions)deviceOptions).DeviceIndex = 2;
        var (options, cfg) = provider.GetChannelOptions();
        Configure(cfg).UseChannelIndex(0).SoftwareFeaturesFallBack(fallback);
        if (rangeFirst) cfg.RangeFilter(0x400, 0x40F, CanFilterIDType.Standard);
        cfg.AccMask(0x24600000U, 0x001FFFFFU, CanFilterIDType.Standard);
        if (!rangeFirst) cfg.RangeFilter(0x400, 0x40F, CanFilterIDType.Standard);
        using var device = provider.Factory.CreateDevice(deviceOptions);
        var transceiver = provider.Factory.CreateTransceivers(device.Options, cfg);

        if (fallback != CanFeature.Filters)
        {
            Action open = () => { using var rx = provider.Factory.CreateBus(device, options, transceiver, provider); };
            open.Should().Throw<CanFilterConfigurationException>().WithMessage("*Enable software fallback*");
            InvokeNative("InitializationCount", 21U, 2U, 0U).Should().Be(0,
                "unsupported combinations must be rejected before native initialization");
            return;
        }

        using var accepted = provider.Factory.CreateBus(device, options, transceiver, provider);
        var (txOptions, txCfg) = provider.GetChannelOptions();
        Configure(txCfg).UseChannelIndex(1);
        var txTransceiver = provider.Factory.CreateTransceivers(device.Options, txCfg);
        using var tx = provider.Factory.CreateBus(device, txOptions, txTransceiver, provider);
        tx.Transmit(new[] { CanFrame.Classic(0x123), CanFrame.Classic(0x300), CanFrame.Classic(0x405) }).Should().Be(3);
        var received = await accepted.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        try
        {
            received.Select(frame => frame.CanFrame.ID).Should().Equal(0x123, 0x405);
            InvokeNative("ActiveRangeCount", 21U, 2U, 0U).Should().Be(0,
                "the complete OR must run in software with the hardware filter open");
        }
        finally
        {
            foreach (var frame in received) frame.CanFrame.Dispose();
        }
    }

    private sealed class CombinedFilterProvider() : ControlCanEProvider(ControlCanDeviceType.VCI_USBCAN_2E_U)
    {
        public override CanFeature StaticFeatures => base.StaticFeatures | CanFeature.MaskFilter;
    }

    [Fact]
    public void Disposal_During_Software_Filter_Does_Not_Dispatch_Frame_Callbacks()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var allocator = new CountingBufferAllocator();
        var filter = new CanFilter();
        filter.SoftwareFilterRules.Add(new PredicateRule(_ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
                throw new TimeoutException("Test did not release the software filter");
            return true;
        }));
        using var rx = (ControlCanBus)CanBus.Open(MaskRx, cfg => Configure(cfg).BufferAllocator(allocator).SetFilter(filter));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        var receivedCallbacks = 0;
        var observedCallbacks = 0;
#pragma warning disable CS0618
        rx.FrameReceived += (_, _) => Interlocked.Increment(ref receivedCallbacks);
#pragma warning restore CS0618
        rx.FrameObserved += (_, _) => Interlocked.Increment(ref observedCallbacks);
        try
        {
            tx.Transmit(CanFrame.Classic(0x123, new byte[] { 1 })).Should().Be(1);
            entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
            rx.Dispose();
            allocator.DisposeCount.Should().Be(0, "the active predicate still borrows the frame");
            release.Set();
            WaitForChannelCleanup(rx);
            receivedCallbacks.Should().Be(0, "disposal happened before callback dispatch");
            observedCallbacks.Should().Be(0);
            allocator.RentCount.Should().Be(1);
            allocator.DisposeCount.Should().Be(1);
        }
        finally
        {
            release.Set();
            rx.Dispose();
            WaitForChannelCleanup(rx);
        }
    }

    [Fact]
    public void Typed_Open_Releases_The_Device_Lease_When_Filter_Validation_Fails()
    {
        InvokeNative("PreserveRangeTablesOnClose", 4U, 2U, true);
        try
        {
            Action open = () =>
            {
                using var rx = ControlCan.Open(ControlCanDeviceType.VCI_USBCAN2, 2, 0, cfg => Configure(cfg)
                    .AccMask(0x24600000U, 0x001FFFFFU, CanFilterIDType.Standard)
                    .AccMask(0x24600000U, 0x001FFFFFU, CanFilterIDType.Standard));
            };
            open.Should().Throw<CanFilterConfigurationException>();
            using var recovered = ControlCan.Open(ControlCanDeviceType.VCI_USBCAN2, 2, 0, cfg => Configure(cfg));
            InvokeNative("OpenCount", 4U, 2U).Should().Be(2, "the failed open must release the last device lease");
        }
        finally
        {
            InvokeNative("PreserveRangeTablesOnClose", 4U, 2U, false);
            using var cleanup = CanBus.Open(MaskRx, cfg => Configure(cfg));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fault_Automatically_Releases_Channel_And_Lease(bool predicateFault)
    {
        var allocator = new CountingBufferAllocator();
        var failure = new InvalidOperationException("Injected terminal receive failure");
        var filter = new CanFilter();
        filter.FilterRules.Add(new FilterRule.Range(0x100, 0x10F));
        if (predicateFault) filter.SoftwareFilterRules.Add(new PredicateRule(_ => throw failure));
        using var rx = (ControlCanBus)CanBus.Open(RangeRx, cfg => Configure(cfg).BufferAllocator(allocator)
            .SoftwareFeaturesFallBack(CanFeature.Filters).SetFilter(filter)
            .ExceptionPolicy(new CanExceptionPolicy { SubscriberCallbackSeverity = CanExceptionSeverity.Fault }));
        using var tx = CanBus.Open(RangeTx, cfg => Configure(cfg));
        var ownerField = typeof(ControlCanBus).GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var owner = new TrackingOwner((IDisposable)ownerField.GetValue(rx)!);
        rx.AttachOwner(owner);
        if (!predicateFault) rx.FrameObserved += (_, _) => throw failure;
        var receiving = rx.ReceiveAsync(1, 5000, TestContext.Current.CancellationToken);
        tx.Transmit(CanFrame.Classic(0x105, new byte[] { 1 })).Should().Be(1);
        Func<Task> receive = async () => { await receiving; };
        (await receive.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        WaitForChannelCleanup(rx);
        owner.DisposeCount.Should().Be(1, "a fault releases the lease without waiting for user disposal");
        InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(false);
        InvokeNative("ActiveRangeCount", 21U, 2U, 0U).Should().Be(0);
        allocator.DisposeCount.Should().Be(allocator.RentCount);

        using var reopened = CanBus.Open(RangeRx, cfg => Configure(cfg));
        Action staleTransmit = () => rx.Transmit(CanFrame.Classic(0x105));
        staleTransmit.Should().Throw<CanBusDisposedException>();
        tx.Transmit(CanFrame.Classic(0x405)).Should().Be(1);
        (await reopened.ReceiveAsync(1, 1000, TestContext.Current.CancellationToken))
            .Select(frame => frame.CanFrame.ID).Should().Equal(0x405);
        rx.Dispose();
        owner.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Early_Receive_Fault_Releases_An_Owner_Attached_After_Cleanup()
    {
        using (CanBus.Open(RangeTx, cfg => Configure(cfg))) { }
        var prepared = ControlCanEndpoint.Prepare(CanEndpoint.Parse(RangeRx), cfg => Configure(cfg)
            .RangeFilter(0x100, 0x10F, CanFilterIDType.Standard));
        using var device = prepared.Provider.Factory.CreateDevice(prepared.DeviceOptions);
        using var rx = (ControlCanBus)prepared.Provider.Factory.CreateBus(device, prepared.BusOptions,
            new FaultingTransceiver(), prepared.Provider);
        WaitForChannelCleanup(rx);
        InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(false);
        InvokeNative("ActiveRangeCount", 21U, 2U, 0U).Should().Be(0);
        var owner = new TrackingOwner(device);
        Action attach = () => rx.AttachOwner(owner);
        attach.Should().Throw<CanBusCreationException>().WithMessage("*stopped while opening*Injected startup receive failure*");
        owner.DisposeCount.Should().Be(1);
        rx.Dispose();
        owner.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Early_Receive_Fault_Retains_An_Attached_Owner_Until_Cleanup_Finishes()
    {
        using (CanBus.Open(RangeTx, cfg => Configure(cfg))) { }
        using var allowFault = new ManualResetEventSlim();
        using var faultEntered = new ManualResetEventSlim();
        using var releaseFault = new ManualResetEventSlim();
        var prepared = ControlCanEndpoint.Prepare(CanEndpoint.Parse(RangeRx), cfg => Configure(cfg));
        using var device = prepared.Provider.Factory.CreateDevice(prepared.DeviceOptions);
        using var rx = (ControlCanBus)prepared.Provider.Factory.CreateBus(device, prepared.BusOptions,
            new FaultingTransceiver(() => allowFault.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)), prepared.Provider);
        rx.FaultOccurred += (_, _) =>
        {
            faultEntered.Set();
            releaseFault.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        };
        var owner = new TrackingOwner(device);
        try
        {
            allowFault.Set();
            faultEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
            Action attach = () => rx.AttachOwner(owner);
            attach.Should().Throw<CanBusCreationException>().WithMessage("*Injected startup receive failure*");
            owner.DisposeCount.Should().Be(0, "the receive task is still executing its fault callback");
            releaseFault.Set();
            WaitForChannelCleanup(rx);
            owner.DisposeCount.Should().Be(1);
        }
        finally
        {
            allowFault.Set();
            releaseFault.Set();
            rx.Dispose();
            WaitForChannelCleanup(rx);
        }
    }

    private sealed class FaultingTransceiver(Action? beforeFault = null) : ITransceiver
    {
        public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
        {
            beforeFault?.Invoke();
            throw new InvalidOperationException("Injected startup receive failure");
        }
        public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<CanFrame> frames) => throw new NotSupportedException();
        public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ReadOnlySpan<CanFrame> frames) => throw new NotSupportedException();
        public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in CanFrame frame) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearBuffer_Discards_Queued_And_InFlight_Frames(bool clearFromCallback)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var allocator = new CountingBufferAllocator();
        using var rx = (ControlCanBus)CanBus.Open(MaskRx, cfg => Configure(cfg).BufferAllocator(allocator));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));
        var oldBatchObserved = 0;
        rx.FrameObserved += (_, data) =>
        {
            if (data.CanFrame.ID == 0x10)
            {
                // Fill a complete native batch before the poll thread can read it.
                tx.Transmit(new[]
                {
                    CanFrame.Classic(0x100, new byte[] { 1 }),
                    CanFrame.Classic(0x101, new byte[] { 2 }),
                    CanFrame.Classic(0x102, new byte[] { 3 })
                });
            }
            if (data.CanFrame.ID == 0x102) Interlocked.Increment(ref oldBatchObserved);
            if (data.CanFrame.ID != 0x101) return;
            if (clearFromCallback) rx.ClearBuffer();
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        };
        try
        {
            tx.Transmit(CanFrame.Classic(0x10, new byte[] { 0 }));
            entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
            if (!clearFromCallback) rx.ClearBuffer();
            tx.Transmit(CanFrame.Classic(0x200, new byte[] { 4 })).Should().Be(1);
            release.Set();
            var received = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
            try
            {
                received.Select(frame => frame.CanFrame.ID).Should().Equal(0x200);
                oldBatchObserved.Should().Be(0);
                InvokeNative("IsStarted", 4U, 2U, 0U).Should().Be(true);
            }
            finally
            {
                foreach (var frame in received) frame.CanFrame.Dispose();
            }
            allocator.RentCount.Should().Be(4, "the remaining old native entry is never materialized");
            allocator.DisposeCount.Should().Be(allocator.RentCount);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task Deferred_Cleanup_Keeps_The_Task_Captured_Before_Cancellation()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var rx = (ControlCanBus)CanBus.Open(RangeRx, cfg => Configure(cfg)
            .RangeFilter(0x100, 0x10F, CanFilterIDType.Standard));
        using var tx = CanBus.Open(RangeTx, cfg => Configure(cfg));
        var ownerField = typeof(ControlCanBus).GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var owner = new TrackingOwner((IDisposable)ownerField.GetValue(rx)!);
        rx.AttachOwner(owner);
        var pollTaskField = typeof(ControlCanBus).GetField("_pollTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var polling = (Task)pollTaskField.GetValue(rx)!;
        var cancellation = (CancellationTokenSource)typeof(ControlCanBus)
            .GetField("_pollCts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rx)!;
        rx.FrameObserved += (_, _) =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        };
        // Inject the shared-field change during cancellation, after StopReceiveLoop captures
        // its task. Cleanup must still wait for that task, regardless of the field's new value.
        using var registration = cancellation.Token.Register(() => pollTaskField.SetValue(rx, null));
        try
        {
            tx.Transmit(CanFrame.Classic(0x105)).Should().Be(1);
            entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
            Action dispose = rx.Dispose;
            dispose.Should().NotThrow();
            owner.DisposeCount.Should().Be(0);
            InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(true);
            InvokeNative("ActiveRangeCount", 21U, 2U, 0U).Should().Be(1);

            release.Set();
            (await Task.WhenAny(polling, Task.Delay(5000, TestContext.Current.CancellationToken)))
                .Should().BeSameAs(polling);
            await polling;
            SpinWait.SpinUntil(() => owner.DisposeCount == 1, TimeSpan.FromSeconds(5)).Should().BeTrue();
            InvokeNative("IsStarted", 21U, 2U, 0U).Should().Be(false);
            InvokeNative("ActiveRangeCount", 21U, 2U, 0U).Should().Be(0);
        }
        finally
        {
            release.Set();
            if (!polling.IsCompleted)
            {
                try { cancellation.Cancel(); }
                catch (ObjectDisposedException) { /* Cleanup may already have disposed the source. */ }
            }
            await polling;
            // Also restore native state when this regression is run against the old code,
            // which throws before scheduling its continuation.
            if (owner.DisposeCount == 0)
            {
                pollTaskField.SetValue(rx, polling);
                typeof(ControlCanBus).GetMethod("CleanupChannel", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(rx, null);
            }
        }
    }

    private static void WaitForChannelCleanup(ControlCanBus bus)
    {
        var pollTask = typeof(ControlCanBus).GetField("_pollTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        SpinWait.SpinUntil(() => pollTask.GetValue(bus) == null, TimeSpan.FromSeconds(5)).Should().BeTrue();
    }
}
#endif
