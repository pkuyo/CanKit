using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Core.Exceptions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Core.Definitions;
using CanKit.Core.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// WithQueuedTx() must work on a bus that was opened without EnableErrorInfo(). The wrapper
/// subscribes to the inner ErrorFrameReceived only when a consumer subscribes to its own.
/// </summary>
public class QueuedCanBusErrorInfoTests
{
    [Fact]
    public async Task WithQueuedTx_Does_Not_Require_EnableErrorInfo_On_The_Inner_Bus()
    {
        var session = "queuedtx" + Guid.NewGuid().ToString("N");
        using var inner = CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
        using var rx = CanBus.Open($"virtual://{session}/1", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

        var wrap = () => inner.WithQueuedTx();
        using var queued = wrap.Should().NotThrow().Subject;

        queued.Transmit(CanFrame.Classic(0x321, new byte[] { 9 })).Should().Be(1);
        var got = await rx.ReceiveAsync(1, 1000, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.ID).Should().Equal(0x321);
    }

    [Fact]
    public void Subscribing_ErrorFrameReceived_Throws_Like_The_Inner_Bus_When_Error_Info_Is_Off()
    {
        var session = "queuedtx" + Guid.NewGuid().ToString("N");
        using var inner = CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
        using var queued = inner.WithQueuedTx();

        var subscribe = () => queued.ErrorFrameReceived += (_, _) => { };
        subscribe.Should().Throw<CanBusConfigurationException>();
    }

    [Fact]
    public void Subscribing_ErrorFrameReceived_Works_When_Error_Info_Is_On()
    {
        var session = "queuedtx" + Guid.NewGuid().ToString("N");
        using var inner = CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000).EnableErrorInfo());
        using var queued = inner.WithQueuedTx();

        EventHandler<ICanErrorInfo> handler = (_, _) => { };
        var subscribe = () => queued.ErrorFrameReceived += handler;
        subscribe.Should().NotThrow();
        var unsubscribe = () => queued.ErrorFrameReceived -= handler;
        unsubscribe.Should().NotThrow();
    }

    [Fact]
    public void Error_Frames_Are_Forwarded_And_The_Inner_Subscription_Follows_The_Outer_One()
    {
        var inner = new ErrorInfoStubBus { AllowErrorInfo = true };
        using var queued = inner.WithQueuedTx();
        inner.ErrorSubscribers.Should().Be(0, "nothing subscribed to the wrapper yet");

        var received = new List<(object? sender, ICanErrorInfo info)>();
        EventHandler<ICanErrorInfo> handler = (s, e) => received.Add((s, e));
        queued.ErrorFrameReceived += handler;
        inner.ErrorSubscribers.Should().Be(1);

        inner.RaiseError(default(DefaultCanErrorInfo));
        received.Should().HaveCount(1);
        received[0].sender.Should().BeSameAs(queued);

        queued.ErrorFrameReceived -= handler;
        inner.ErrorSubscribers.Should().Be(0, "the last outer unsubscribe releases the inner subscription");

        inner.RaiseError(default(DefaultCanErrorInfo));
        received.Should().HaveCount(1);
    }

    [Fact]
    public void Dispose_Detaches_The_Forwarders_From_The_Inner_Bus()
    {
        var inner = new ErrorInfoStubBus { AllowErrorInfo = true };
        var queued = inner.WithQueuedTx();
        queued.ErrorFrameReceived += (_, _) => { };
        inner.FaultSubscribers.Should().Be(1);
        inner.ErrorSubscribers.Should().Be(1);

        queued.Dispose();

        inner.FaultSubscribers.Should().Be(0);
        inner.ErrorSubscribers.Should().Be(0);
    }

    /// <summary>Stub with the adapters' subscription rule for ErrorFrameReceived.</summary>
    private sealed class ErrorInfoStubBus : ICanBus
    {
        private EventHandler<ICanErrorInfo>? _error;
        private EventHandler<Exception>? _fault;

        public bool AllowErrorInfo { get; set; }
        public int ErrorSubscribers => _error?.GetInvocationList().Length ?? 0;
        public int FaultSubscribers => _fault?.GetInvocationList().Length ?? 0;

        public void RaiseError(ICanErrorInfo info) => _error?.Invoke(this, info);

        public event EventHandler<ICanErrorInfo>? ErrorFrameReceived
        {
            add
            {
                if (!AllowErrorInfo)
                    throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
                _error += value;
            }
            remove => _error -= value;
        }

        public event EventHandler<Exception>? FaultOccurred
        {
            add => _fault += value;
            remove => _fault -= value;
        }

        public int Transmit(IEnumerable<CanFrame> frames, int timeOut = 0) => frames.Count();
        public int Transmit(ReadOnlySpan<CanFrame> frames, int timeOut = 0) => frames.Length;
        public int Transmit(CanFrame[] frames, int timeOut = 0) => frames.Length;
        public int Transmit(ArraySegment<CanFrame> frames, int timeOut = 0) => frames.Count;
        public int Transmit(in CanFrame frame) => 1;

        public Task<int> TransmitAsync(IEnumerable<CanFrame> frames, int timeOut = 0,
            CancellationToken cancellationToken = default) => Task.FromResult(Transmit(frames, timeOut));

        public Task<int> TransmitAsync(CanFrame frame, CancellationToken cancellationToken = default)
            => Task.FromResult(1);

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
        public event EventHandler<Exception>? BackgroundExceptionOccurred;
#pragma warning restore CS0067

        public void Dispose() { }
    }
}
