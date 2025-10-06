using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;

namespace CanKit.Adapter.Virtual;

public sealed class VirtualBus : ICanBus<VirtualBusRtConfigurator>, ICanApplier, IBusOwnership
{
    private readonly object _evtGate = new();

    private readonly IBusOptions _options;
    private readonly ITransceiver _transceiver;

    private readonly VirtualBusHub _hub;

    private readonly AsyncFramePipe _asyncRx;
    private readonly ConcurrentQueue<CanReceiveData> _rxQueue = new();
    private readonly ConcurrentQueue<ICanErrorInfo> _errQueue = new();

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
        _hub = VirtualBusHub.Get(Options.SessionId ?? "default");
        _hub.Attach(this);

        var cap = Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : (int?)null;
        _asyncRx = new AsyncFramePipe(cap);

        // apply initial options (software filter, etc.)
        _options.Apply(this, true);
    }

    internal VirtualBusHub Hub => _hub;

    public void AttachOwner(IDisposable owner)
    {
        _owner = owner;
    }

    public void Apply(ICanOptions options)
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

    public CanOptionType ApplierStatus => CanOptionType.Runtime;

    public void Reset()
    {
        ThrowIfDisposed();
        ClearBuffer();
    }

    public void ClearBuffer()
    {
        while (_rxQueue.TryDequeue(out _)) { }
    }

    public uint Transmit(IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames, timeOut);
    }

    public IPeriodicTx TransmitPeriodic(CanTransmitData frame, PeriodicTxOptions options)
    {
        ThrowIfDisposed();
        if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) == 0)
            throw new CanFeatureNotSupportedException(CanFeature.CyclicTx, Options.Features);
        return SoftwarePeriodicTx.Start(this, frame, options);
    }

    public float BusUsage() => throw new NotSupportedException();

    public CanErrorCounters ErrorCounters() => _hub.GetErrorCounters();

    public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = 0)
    {
        ThrowIfDisposed();
        var startTime = DateTime.Now.Millisecond;
        var list = new List<CanReceiveData>((int)Math.Max(1, Math.Min(count, 256)));
        while (list.Count < count && DateTime.Now.Millisecond - startTime <= timeOut)
        {
            while (list.Count < count && _rxQueue.TryDequeue(out var item))
            {
                list.Add(item);
            }
        }
        return list;
    }

    public Task<uint> TransmitAsync(IEnumerable<CanTransmitData> frames, int timeOut = 0, CancellationToken cancellationToken = default)
        => Task.Run(() => Transmit(frames, timeOut), cancellationToken);

    public Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(uint count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
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

    public bool ReadErrorInfo(out ICanErrorInfo? errorInfo)
    {
        if (_errQueue.TryDequeue(out var e))
        {
            errorInfo = e;
            return true;
        }
        errorInfo = null;
        return false;
    }

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

    public event EventHandler<ICanErrorInfo>? ErrorOccurred
    {
        add
        {
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
        _errQueue.Enqueue(info);
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
}

