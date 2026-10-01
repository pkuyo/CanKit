using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Core.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Core-only tests for the queued TX wrapper. They run against a recording stub bus,
/// so no adapter or hardware is involved.
/// </summary>
public class QueuedCanBusTests
{
    [Fact]
    public async Task Single_Frame_Is_Forwarded_Exactly_Once_Without_Padding_Frames()
    {
        var inner = new RecordingBus();
        using var queued = inner.WithQueuedTx(new QueuedCanBusOptions { SendBatchSize = 8 });

        queued.Transmit(CanFrame.Classic(0x123, new byte[] { 1, 2, 3 })).Should().Be(1);

        await WaitUntilAsync(() => inner.Count >= 1);
        // Give the worker a moment so that surplus frames, if any, would show up.
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var frames = inner.Snapshot();
        frames.Should().HaveCount(1, "only the enqueued frame may reach the driver");
        frames[0].FrameKind.Should().Be(CanFrameType.Can20);
        frames[0].ID.Should().Be(0x123);
        queued.GetTxStats().DrvAccepted.Should().Be(1);
    }

    [Fact]
    public async Task Frames_Are_Forwarded_In_Order_Without_Duplicates_When_Driver_Accepts_One_Per_Call()
    {
        var inner = new RecordingBus { AcceptPerCall = 1 };
        using var queued = inner.WithQueuedTx(new QueuedCanBusOptions { SendBatchSize = 8 });

        var ids = Enumerable.Range(0x100, 20).ToArray();
        foreach (var id in ids)
            queued.Transmit(CanFrame.Classic(id, new byte[] { (byte)id })).Should().Be(1);

        await WaitUntilAsync(() => inner.Count >= ids.Length);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        inner.Snapshot().Select(f => f.ID).Should().Equal(ids);
    }

    [Fact]
    public async Task Held_Back_Frames_Are_Retried_Without_A_New_Enqueue()
    {
        // The driver is busy for the first calls and accepts nothing.
        var inner = new RecordingBus { BusyCalls = 3 };
        using var queued = inner.WithQueuedTx(new QueuedCanBusOptions { SendBatchSize = 8 });

        queued.Transmit(CanFrame.Classic(0x201, new byte[] { 1 })).Should().Be(1);
        queued.Transmit(CanFrame.Classic(0x202, new byte[] { 2 })).Should().Be(1);

        // Nothing else is enqueued: the wrapper has to retry on its own.
        await WaitUntilAsync(() => inner.Count >= 2);

        inner.Snapshot().Select(f => f.ID).Should().Equal(0x201, 0x202);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Minimal ICanBus that records every frame handed to any Transmit overload.
    /// </summary>
    private sealed class RecordingBus : ICanBus
    {
        private readonly object _gate = new();
        private readonly List<CanFrame> _frames = new();
        private int _calls;

        /// <summary>Maximum number of frames accepted per call (default: all).</summary>
        public int AcceptPerCall { get; set; } = int.MaxValue;

        /// <summary>Number of initial calls that accept nothing.</summary>
        public int BusyCalls { get; set; }

        public int Count
        {
            get { lock (_gate) return _frames.Count; }
        }

        public CanFrame[] Snapshot()
        {
            lock (_gate) return _frames.ToArray();
        }

        private int Record(ReadOnlySpan<CanFrame> frames)
        {
            lock (_gate)
            {
                if (_calls++ < BusyCalls) return 0;
                var n = Math.Min(frames.Length, AcceptPerCall);
                for (var i = 0; i < n; i++) _frames.Add(frames[i]);
                return n;
            }
        }

        public int Transmit(IEnumerable<CanFrame> frames, int timeOut = 0) => Record(frames.ToArray());
        public int Transmit(ReadOnlySpan<CanFrame> frames, int timeOut = 0) => Record(frames);
        public int Transmit(CanFrame[] frames, int timeOut = 0) => Record(frames);
        public int Transmit(ArraySegment<CanFrame> frames, int timeOut = 0) => Record(frames.AsSpan());
        public int Transmit(in CanFrame frame) => Record(new[] { frame });

        public Task<int> TransmitAsync(IEnumerable<CanFrame> frames, int timeOut = 0,
            CancellationToken cancellationToken = default) => Task.FromResult(Transmit(frames, timeOut));

        public Task<int> TransmitAsync(CanFrame frame, CancellationToken cancellationToken = default)
            => Task.FromResult(Transmit(frame));

        public IBusRTOptionsConfigurator Options => throw new NotSupportedException();
        public BusState BusState => BusState.None;
        public BusNativeHandle NativeHandle => default;
        public void Reset() { }
        public void ClearBuffer() { }
        public float BusUsage() => 0;
        public CanErrorCounters ErrorCounters() => default;

        public IPeriodicTx TransmitPeriodic(CanFrame frame, PeriodicTxOptions options)
            => throw new NotSupportedException();

        public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0)
            => Array.Empty<CanReceiveData>();

        public Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CanReceiveData>>(Array.Empty<CanReceiveData>());

        public async IAsyncEnumerable<CanReceiveData> GetFramesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

#pragma warning disable CS0067 // events are part of the contract but unused by the stub
        public event EventHandler<CanReceiveData>? FrameReceived;
        public event EventHandler<CanReceiveDataView>? FrameObserved;
        public event EventHandler<ICanErrorInfo>? ErrorFrameReceived;
        public event EventHandler<Exception>? BackgroundExceptionOccurred;
        public event EventHandler<Exception>? FaultOccurred;
#pragma warning restore CS0067

        public void Dispose() { }
    }
}
