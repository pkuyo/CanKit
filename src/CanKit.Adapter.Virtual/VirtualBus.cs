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
        _useSoftwareFilter = true;
        _softwareFilterPredicate = Options.Filter.FilterRules.Count > 0
            ? FilterRule.Build(Options.Filter.FilterRules)
            : null;
    }

    public BusNativeHandle NativeHandle { get; } = default;

    public void Reset()
    {
        ThrowIfDisposed();
        ClearBuffer();
    }

    public void ClearBuffer()
    {
    }

    //non-support time out
    public int Transmit(IEnumerable<ICanFrame> frames, int _ = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames);
    }

    public int Transmit(ReadOnlySpan<ICanFrame> frames, int _ = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames);
    }

    public int Transmit(ICanFrame[] frames, int _ = 0)
        => Transmit(frames.AsSpan());

    public int Transmit(ArraySegment<ICanFrame> frames, int _ = 0)
        => Transmit(frames.AsSpan());

    public int Transmit(in ICanFrame frame)
        => _transceiver.Transmit(this, frame);

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

    public Task<int> TransmitAsync(ICanFrame frame, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            try { return Transmit(frame); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { HandleBackgroundException(ex); throw; }
        }, cancellationToken);

    public async Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return await _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken)
            .ConfigureAwait(false);
    }

#if NET8_0_OR_GREATER
    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await foreach (var item in _asyncRx.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
#endif

    public VirtualBusRtConfigurator Options { get; }

    IBusRTOptionsConfigurator ICanBus.Options => Options;

    public event EventHandler<CanReceiveData>? FrameReceived
    {
        add
        {
            lock (_evtGate)
            {
                _frameReceived += value;
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _frameReceived -= value;
            }
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
            lock (_evtGate)
            {
                _errorOccurred += value;
            }
        }
        remove
        {
            if (!Options.AllowErrorInfo)
            {
                throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
            }
            lock (_evtGate)
            {
                _errorOccurred -= value;
            }
        }
    }

    public BusState BusState => _hub.GetBusState();

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            _hub.Detach(this);
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
        if (_useSoftwareFilter && pred is not null && !pred(data.CanFrame))
        {
            return;
        }

        _frameReceived?.Invoke(this, data);
        _asyncRx.Publish(data);

    }

    internal void InternalInjectError(ICanErrorInfo info)
    {
        _errorOccurred?.Invoke(this, info);
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
