using System;
using System.Buffers;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class VirtualBusDisposeQueueTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(0, true)]
    [InlineData(4, true)]
    public async Task Dispose_Discards_Queued_Frames_Without_Releasing_The_Senders_Buffer(
        int capacity, bool disposeDuringDelivery)
    {
        var session = $"dispose-queue-{Guid.NewGuid():N}";
        using var rx = CanBus.Open($"virtual://{session}/0", cfg =>
        {
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000);
            if (capacity > 0)
                cfg.SetAsyncBufferCapacity(capacity);
        });
        using var tx = CanBus.Open($"virtual://{session}/1", cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
        await using var reader = rx.GetFramesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        // Start the enumeration and leave it suspended while later frames queue up.
        tx.Transmit(CanFrame.Classic(0x100, new byte[] { 1 })).Should().Be(1);
        (await reader.MoveNextAsync()).Should().BeTrue();

        var owner = new TrackedOwner();
        using var frame = CanFrame.Classic(0x123, owner);
        tx.Transmit(frame).Should().Be(1);
        tx.Transmit(frame).Should().Be(1);

        if (disposeDuringDelivery)
        {
            // The broadcast already holds this receiver; Publish happens after the callback.
            rx.FrameObserved += (_, _) => rx.Dispose();
            tx.Transmit(frame).Should().Be(1);
        }
        else
        {
            rx.Dispose();
        }

        owner.DisposeCount.Should().Be(0, "receivers queue their own copies, not the sender's payload");
        frame.Data.Span[0].Should().Be(42);
        (await reader.MoveNextAsync().AsTask().WithinTwoSeconds()).Should().BeFalse(
            "disposal must discard unread frames before the enumeration resumes");
    }

    private sealed class TrackedOwner : IMemoryOwner<byte>
    {
        private byte[]? _buffer = { 42 };

        public int DisposeCount { get; private set; }
        public Memory<byte> Memory => _buffer ?? throw new ObjectDisposedException(nameof(TrackedOwner));

        public void Dispose()
        {
            DisposeCount++;
            _buffer = null;
        }
    }
}
