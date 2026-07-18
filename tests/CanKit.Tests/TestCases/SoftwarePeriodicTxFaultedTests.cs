using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;
using CanKit.Core.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Unit tests for the <c>Faulted</c> event added to <see cref="SoftwarePeriodicTx"/>. The
/// loop must surface inner-bus transmit failures to subscribers while keeping the schedule
/// alive so transient errors do not silently end long-running native L1 periodic sends
/// (unblocks the deferred J1939 native <c>IPeriodicTx</c> path — see
/// <c>src/protocols/CanKit.Pro.J1939/README.md</c> and the deferral note in
/// <c>J1939NodeImpl.cs</c>).
/// </summary>
public class SoftwarePeriodicTxFaultedTests
{
    [Fact]
    public void SoftwarePeriodicTx_Raises_Faulted_When_Inner_Transmit_Throws_And_Loop_Keeps_Running()
    {
        var bus = new AlwaysThrowingBus();
        using var faultObserved = new ManualResetEventSlim();
        var faults = new List<Exception>();

        using var periodic = SoftwarePeriodicTx.Create(
            bus,
            CanFrame.Classic(0x123, new byte[] { 0xAA }),
            new PeriodicTxOptions(TimeSpan.FromMilliseconds(5), repeat: -1, fireImmediately: true));

        periodic.Faulted += (_, ex) =>
        {
            lock (faults)
            {
                faults.Add(ex);
            }
            faultObserved.Set();
        };

        periodic.Start();

        faultObserved.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue(
            "Faulted must fire when the inner bus Transmit throws");

        // Poll for further transmit attempts to prove the loop survives the initial fault.
        // Uses SpinWait with a generous timeout so slow/loaded CI hosts don't flake here,
        // in contrast to a fixed Thread.Sleep which either bloats runtime or races.
        var loopKeptRunning = SpinWait.SpinUntil(
            () => bus.TransmitCount > 1,
            TimeSpan.FromSeconds(2));

        periodic.Stop();
        periodic.IsRunning.Should().BeFalse();
        loopKeptRunning.Should().BeTrue(
            "the periodic loop must keep attempting transmits after a fault (loop-alive semantics)");
        bus.TransmitCount.Should().BeGreaterThan(1,
            "the periodic loop must keep attempting transmits after a fault (loop-alive semantics)");

        lock (faults)
        {
            faults.Should().NotBeEmpty();
            faults[0].Should().BeOfType<InvalidOperationException>();
        }
    }

    [Fact]
    public void SoftwarePeriodicTx_Faulted_Handler_May_Call_Update_Without_Deadlock()
    {
        // Faulted must be raised outside the internal _gate (same reentrancy contract as
        // Completed), so a handler that calls back into Update/Stop must not deadlock.
        var bus = new AlwaysThrowingBus();
        using var handlerReturned = new ManualResetEventSlim();
        Exception? handlerException = null;

        using var periodic = SoftwarePeriodicTx.Create(
            bus,
            CanFrame.Classic(0x321, new byte[] { 0x01 }),
            new PeriodicTxOptions(TimeSpan.FromMilliseconds(5), repeat: -1, fireImmediately: true));

        periodic.Faulted += (sender, _) =>
        {
            try
            {
                ((IPeriodicTx)sender!).Update(period: TimeSpan.FromMilliseconds(10));
            }
            catch (Exception ex)
            {
                handlerException = ex;
            }
            finally
            {
                handlerReturned.Set();
            }
        };

        periodic.Start();
        handlerReturned.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        handlerException.Should().BeNull();
        periodic.Stop();
    }

    /// <summary>
    /// Minimal ICanBus fake for periodic-loop unit tests: every <c>Transmit</c> throws so
    /// the loop always fails a tick. Only the members actually reached by
    /// <see cref="SoftwarePeriodicTx"/> are implemented; the rest throw
    /// <see cref="NotImplementedException"/> to fail loudly if the loop starts touching
    /// APIs it does not need.
    /// </summary>
    private sealed class AlwaysThrowingBus : ICanBus
    {
        private int _transmitCount;

        public int TransmitCount => Volatile.Read(ref _transmitCount);

        public IBusRTOptionsConfigurator Options => throw new NotImplementedException();
        public BusState BusState => BusState.Unknown;
        public BusNativeHandle NativeHandle => default;

        public void Reset() => throw new NotImplementedException();
        public void ClearBuffer() => throw new NotImplementedException();

        public int Transmit(IEnumerable<CanFrame> frames, int timeOut = 0)
        {
            Interlocked.Increment(ref _transmitCount);
            throw new InvalidOperationException("simulated inner bus failure");
        }

        public int Transmit(ReadOnlySpan<CanFrame> frames, int timeOut = 0)
        {
            Interlocked.Increment(ref _transmitCount);
            throw new InvalidOperationException("simulated inner bus failure");
        }

        public int Transmit(CanFrame[] frames, int timeOut = 0)
        {
            Interlocked.Increment(ref _transmitCount);
            throw new InvalidOperationException("simulated inner bus failure");
        }

        public int Transmit(ArraySegment<CanFrame> frames, int timeOut = 0)
        {
            Interlocked.Increment(ref _transmitCount);
            throw new InvalidOperationException("simulated inner bus failure");
        }

        public int Transmit(in CanFrame frame)
        {
            Interlocked.Increment(ref _transmitCount);
            throw new InvalidOperationException("simulated inner bus failure");
        }

        public Task<int> TransmitAsync(IEnumerable<CanFrame> frames, int timeOut = 0,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> TransmitAsync(CanFrame frame, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public IPeriodicTx TransmitPeriodic(CanFrame frame, PeriodicTxOptions options)
            => throw new NotImplementedException();

        public float BusUsage() => throw new NotImplementedException();
        public CanErrorCounters ErrorCounters() => throw new NotImplementedException();

        public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<CanReceiveData> GetFramesAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

#pragma warning disable CS0067
        public event EventHandler<CanReceiveData>? FrameReceived;
        public event EventHandler<CanReceiveDataView>? FrameObserved;
        public event EventHandler<ICanErrorInfo>? ErrorFrameReceived;
        public event EventHandler<Exception>? BackgroundExceptionOccurred;
        public event EventHandler<Exception>? FaultOccurred;
#pragma warning restore CS0067

        public void Dispose() { }
    }
}
