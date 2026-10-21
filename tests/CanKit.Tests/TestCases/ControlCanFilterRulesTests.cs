#if FAKE
using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Adapter.ControlCAN;
using CanKit.Core;
using CanKit.Core.Definitions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Filter rules are combined with OR. The hardware takes the list only when it can express it as
/// a whole; any other list is evaluated in software with the hardware filter left open, so the
/// OR is preserved. Building that list must not modify the rule list being iterated.
/// </summary>
public class ControlCanFilterRulesTests
{
    // Dedicated fake devices; index 0 belongs to the matrix, index 1 to ControlCanRangeFilterTests.
    private const string RangeRx = "controlcan://VCI_USBCAN_2E_U?index=2#ch0";
    private const string RangeTx = "controlcan://VCI_USBCAN_2E_U?index=2#ch1";
    private const string MaskRx = "controlcan://VCI_USBCAN2?index=2#ch0";
    private const string MaskTx = "controlcan://VCI_USBCAN2?index=2#ch1";

    [Fact]
    public async Task Range_And_Mask_Rules_Are_Evaluated_In_Software_As_A_Union()
    {
        using var rx = CanBus.Open(RangeRx, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.MaskFilter)
            .RangeFilter(0x100, 0x1FF, CanFilterIDType.Standard)
            .AccMask(0x400, 0x7F0, CanFilterIDType.Standard));
        using var tx = CanBus.Open(RangeTx, cfg => Configure(cfg));

        rx.Options.Filter.SoftwareFilterRules.Should().HaveCount(2, "a mixed list cannot be split without losing the OR");

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
            .AccMask(0x100, 0x7F0, CanFilterIDType.Standard)
            .AccMask(0x400, 0x7F0, CanFilterIDType.Standard));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));

        rx.Options.Filter.SoftwareFilterRules.Should().HaveCount(2);

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
                cfg.AccMask(0x100, 0x7F0, CanFilterIDType.Standard);
            cfg.AccMask(0x400, 0x7F0, CanFilterIDType.Standard);
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
                cfg.AccMask(0x100, 0x7F0, CanFilterIDType.Standard);
            cfg.AccMask(0x400, 0x7F0, CanFilterIDType.Standard);
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
}
#endif
