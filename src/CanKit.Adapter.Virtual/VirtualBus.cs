using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;

namespace CanKit.Adapter.Virtual;

public sealed class VirtualBus : ICanBus<VirtualBusRtConfigurator>, IBusOwnership
{
    private readonly object _evtGate = new();

    private readonly IBusOptions _options;
    private readonly ITransceiver _transceiver;

    private readonly VirtualBusHub _hub;

    private readonly AsyncFramePipe _asyncRx;
    private readonly ConcurrentQueue<CanReceiveData> _rxQueue = new();

    private Func<ICanFrame, bool>? _softwareFilterPredicate;
    private bool _useSoftwareFilter;
    private int _subscriberCount;
    private int _asyncConsumerCount;
    private CancellationTokenSource? _stopDelayCts;

    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<ICanErrorInfo>? _errorOccurred;
    private IDisposable? _owner;
    private bool _disposed;

    internal VirtualBus(IBusOptions options, ITransceiver transceiver)
    {
        Options = new VirtualBusRtConfigurator();
        Options.Init((VirtualBusOptions)options);
        _options = options;
        _transceiver = transceiver;

        // join hub
        _hub = VirtualBusHub.Get(Options.SessionId);
        _hub.Attach(this);

        var cap = Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : (int?)null;
        _asyncRx = new AsyncFramePipe(cap);

        // apply initial options (software filter, etc.)
        ApplyConfig(_options);
    }

    internal VirtualBusHub Hub => _hub;

    public void AttachOwner(IDisposable owner)
    {
        _owner = owner;
    }

    public void ApplyConfig(ICanOptions options)
    {
        if (options is not VirtualBusOptions vo)
        {
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(VirtualBusOptions), options.GetType(), "channel");
        }

        // software filter only
        _useSoftwareFilter = (Options.EnabledSoftwareFallback & CanFeature.Filters) != 0;
        _softwareFilterPredicate = Options.Filter.FilterRules.Count > 0
            ? FilterRule.Build(Options.Filter.FilterRules)
            : null;
    }

    public void Reset()
    {
        ThrowIfDisposed();
        ClearBuffer();
    }

    public void ClearBuffer()
    {
        while (_rxQueue.TryDequeue(out _)) { }
    }

    //non-support time out
    public int Transmit(IEnumerable<ICanFrame> frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames, timeOut);
    }

    public IPeriodicTx TransmitPeriodic(ICanFrame frame, PeriodicTxOptions options)
    {
        ThrowIfDisposed();
        if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) == 0)
            throw new CanFeatureNotSupportedException(CanFeature.CyclicTx, Options.Features);
        return SoftwarePeriodicTx.Start(this, frame, options);
    }

    public float BusUsage() => throw new NotSupportedException();

    public CanErrorCounters ErrorCounters() => _hub.GetErrorCounters();

    public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return ReceiveAsync(count, timeOut).GetAwaiter().GetResult();
    }

    public Task<int> TransmitAsync(IEnumerable<ICanFrame> frames, int timeOut = 0, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            try
            {
                return Transmit(frames, timeOut);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { HandleBackgroundException(ex); throw; }
        }, cancellationToken);

    public Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        Interlocked.Increment(ref _subscriberCount);
        Interlocked.Increment(ref _asyncConsumerCount);
        return _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken)
            .ContinueWith(t =>
            {
                var remAsync = Interlocked.Decrement(ref _asyncConsumerCount);
                var rem = Interlocked.Decrement(ref _subscriberCount);
                if (rem == 0 && remAsync == 0) RequestStopReceiveDelay();
                return t.Result;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

#if NET8_0_OR_GREATER
    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _subscriberCount);
        Interlocked.Increment(ref _asyncConsumerCount);
        try
        {
            await foreach (var item in _asyncRx.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }
        finally
        {
            var remAsync = Interlocked.Decrement(ref _asyncConsumerCount);
            var rem = Interlocked.Decrement(ref _subscriberCount);
            if (rem == 0 && remAsync == 0) RequestStopReceiveDelay();
        }
    }
#endif

    public VirtualBusRtConfigurator Options { get; }

    IBusRTOptionsConfigurator ICanBus.Options => Options;

    public event EventHandler<CanReceiveData>? FrameReceived
    {
        add
        {
            bool inc = false;
            lock (_evtGate)
            {
                var before = _frameReceived;
                _frameReceived += value;
                if (!ReferenceEquals(before, _frameReceived))
                {
                    inc = before == null;
                    Interlocked.Increment(ref _subscriberCount);
                }
            }
            if (inc) CancelPendingStopDelay();
        }
        remove
        {
            bool needStop = false;
            lock (_evtGate)
            {
                var before = _frameReceived;
                _frameReceived -= value;
                if (!ReferenceEquals(before, _frameReceived))
                {
                    var now = Interlocked.Decrement(ref _subscriberCount);
                    if (now == 0 && Volatile.Read(ref _asyncConsumerCount) == 0)
                        needStop = true;
                }
            }
            if (needStop) RequestStopReceiveDelay();
        }
    }

    public event EventHandler<ICanErrorInfo>? ErrorFrameReceived
    {
        add
        {
            if (!Options.AllowErrorInfo)
            {
                throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
            }
            bool inc = false;
            lock (_evtGate)
            {
                var before = _errorOccurred;
                _errorOccurred += value;
                if (!ReferenceEquals(before, _errorOccurred))
                {
                    inc = before == null;
                    Interlocked.Increment(ref _subscriberCount);
                }
            }
            if (inc) CancelPendingStopDelay();
        }
        remove
        {
            if (!Options.AllowErrorInfo)
            {
                throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
            }
            bool needStop = false;
            lock (_evtGate)
            {
                var before = _errorOccurred;
                _errorOccurred -= value;
                if (!ReferenceEquals(before, _errorOccurred))
                {
                    var now = Interlocked.Decrement(ref _subscriberCount);
                    if (now == 0 && Volatile.Read(ref _asyncConsumerCount) == 0)
                        needStop = true;
                }
            }
            if (needStop) RequestStopReceiveDelay();
        }
    }

    public BusState BusState => _hub.GetBusState();

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            _hub.Detach(this);
            CancelPendingStopDelay();
        }
        finally
        {
            _disposed = true;
            _owner?.Dispose();
        }
    }

    internal void InternalDeliver(CanReceiveData data)
    {
        var pred = _softwareFilterPredicate;
        if (!_useSoftwareFilter || pred is null || pred(data.CanFrame))
        {
            _rxQueue.Enqueue(data);
            _frameReceived?.Invoke(this, data);
            if (Volatile.Read(ref _asyncConsumerCount) > 0)
                _asyncRx.Publish(data);
        }
    }

    internal void InternalInjectError(ICanErrorInfo info)
    {
        _errorOccurred?.Invoke(this, info);
    }

    private void RequestStopReceiveDelay()
    {
        CancelPendingStopDelay();
        if (Options.ReceiveLoopStopDelayMs <= 0) return;
        _stopDelayCts = new CancellationTokenSource();
        var token = _stopDelayCts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(Options.ReceiveLoopStopDelayMs, token).ConfigureAwait(false); }
            catch { /* ignore */ }
        }, token);
    }

    private void CancelPendingStopDelay()
    {
        try { _stopDelayCts?.Cancel(); }
        catch { }
        finally { _stopDelayCts?.Dispose(); _stopDelayCts = null; }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
    }

    public event EventHandler<Exception>? BackgroundExceptionOccurred;

    private void HandleBackgroundException(Exception ex)
    {
        try { CanKitLogger.LogError("Virtual bus occured background exception.", ex); } catch { }
        try { _asyncRx.ExceptionOccured(ex); } catch { }
        try { var snap = Volatile.Read(ref BackgroundExceptionOccurred); snap?.Invoke(this, ex); } catch { }
    }
}
