using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Abstractions.SPI.Common;

namespace CanKit.Core.Utils;

internal sealed class SoftwareBusUsageCanBus : ICanBus
{
    private readonly ICanBus _inner;
    private readonly SoftwareBusUsageEstimator _estimator = new();
    private readonly IBusRTOptionsConfigurator _options;
    private readonly EventHandler<CanReceiveData> _onFrameReceived;
    private readonly EventHandler<CanReceiveDataView> _onFrameObserved;
    private readonly EventHandler<ICanErrorInfo> _onErrorFrameReceived;
    private readonly EventHandler<Exception> _onBackgroundException;
    private readonly EventHandler<Exception> _onFault;
    private readonly object _errorEventGate = new();
    private EventHandler<ICanErrorInfo>? _errorFrameReceived;
    private bool _disposed;

    internal SoftwareBusUsageCanBus(ICanBus inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = new OptionsView(inner.Options);
        _onFrameReceived = (sender, data) => FrameReceived?.Invoke(this, data);
        _onFrameObserved = (sender, data) =>
        {
            if (!data.IsEcho) _estimator.Observe(data.CanFrame);
            FrameObserved?.Invoke(this, data);
        };
        _onErrorFrameReceived = (sender, error) => _errorFrameReceived?.Invoke(this, error);
        _onBackgroundException = (sender, error) => BackgroundExceptionOccurred?.Invoke(this, error);
        _onFault = (sender, error) => FaultOccurred?.Invoke(this, error);
        _inner.FrameReceived += _onFrameReceived;
        _inner.FrameObserved += _onFrameObserved;
        _inner.BackgroundExceptionOccurred += _onBackgroundException;
        _inner.FaultOccurred += _onFault;
    }

    public IBusRTOptionsConfigurator Options => _options;
    public BusState BusState => _inner.BusState;
    public BusNativeHandle NativeHandle => _inner.NativeHandle;
    public void Reset() => _inner.Reset();
    public void ClearBuffer() => _inner.ClearBuffer();

    public float BusUsage()
    {
        var timing = _inner.Options.BitTiming.Classic;
        if (timing?.Nominal.Bitrate is not uint bitrate)
        {
            if (timing?.Nominal.Segments is { } segments && timing.Value.clockMHz is uint clockMHz)
                bitrate = segments.BitRate(clockMHz);
            else
                throw new InvalidOperationException(
                    "Software bus usage requires a resolvable classical CAN bitrate.");
        }
        return _estimator.GetUsage(bitrate);
    }

    public CanErrorCounters ErrorCounters() => _inner.ErrorCounters();
    public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0) => _inner.Receive(count, timeOut);
    public Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0,
        CancellationToken cancellationToken = default) => _inner.ReceiveAsync(count, timeOut, cancellationToken);
    public IAsyncEnumerable<CanReceiveData> GetFramesAsync(CancellationToken cancellationToken = default)
        => _inner.GetFramesAsync(cancellationToken);
    public IPeriodicTx TransmitPeriodic(CanFrame frame, PeriodicTxOptions options)
    {
        if ((_inner.Options.EnabledSoftwareFallback & CanFeature.CyclicTx) != 0)
            return SoftwarePeriodicTx.Start(this, frame, options);
        return _inner.TransmitPeriodic(frame, options);
    }

    public int Transmit(IEnumerable<CanFrame> frames, int timeOut = 0) => Transmit(frames.ToArray(), timeOut);
    public int Transmit(ReadOnlySpan<CanFrame> frames, int timeOut = 0)
    {
        var accepted = _inner.Transmit(frames, timeOut);
        ObserveAccepted(frames, accepted);
        return accepted;
    }
    public int Transmit(CanFrame[] frames, int timeOut = 0) => Transmit(frames.AsSpan(), timeOut);
    public int Transmit(ArraySegment<CanFrame> frames, int timeOut = 0) => Transmit(frames.AsSpan(), timeOut);
    public int Transmit(in CanFrame frame)
    {
        var accepted = _inner.Transmit(frame);
        if (accepted > 0) _estimator.Observe(frame);
        return accepted;
    }

    public async Task<int> TransmitAsync(IEnumerable<CanFrame> frames, int timeOut = 0,
        CancellationToken cancellationToken = default)
    {
        var batch = frames.ToArray();
        var accepted = await _inner.TransmitAsync(batch, timeOut, cancellationToken).ConfigureAwait(false);
        ObserveAccepted(batch, accepted);
        return accepted;
    }

    public async Task<int> TransmitAsync(CanFrame frame, CancellationToken cancellationToken = default)
    {
        var accepted = await _inner.TransmitAsync(frame, cancellationToken).ConfigureAwait(false);
        if (accepted > 0) _estimator.Observe(frame);
        return accepted;
    }

    private void ObserveAccepted(ReadOnlySpan<CanFrame> frames, int accepted)
    {
        var count = Math.Min(Math.Max(accepted, 0), frames.Length);
        for (var index = 0; index < count; index++) _estimator.Observe(frames[index]);
    }

    public event EventHandler<CanReceiveData>? FrameReceived;
    public event EventHandler<CanReceiveDataView>? FrameObserved;
    public event EventHandler<ICanErrorInfo>? ErrorFrameReceived
    {
        add
        {
            if (value is null) return;
            lock (_errorEventGate)
            {
                if (_errorFrameReceived is null) _inner.ErrorFrameReceived += _onErrorFrameReceived;
                _errorFrameReceived += value;
            }
        }
        remove
        {
            if (value is null) return;
            lock (_errorEventGate)
            {
                _errorFrameReceived -= value;
                if (_errorFrameReceived is null) _inner.ErrorFrameReceived -= _onErrorFrameReceived;
            }
        }
    }
    public event EventHandler<Exception>? BackgroundExceptionOccurred;
    public event EventHandler<Exception>? FaultOccurred;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inner.FrameReceived -= _onFrameReceived;
        _inner.FrameObserved -= _onFrameObserved;
        lock (_errorEventGate)
        {
            if (_errorFrameReceived is not null) _inner.ErrorFrameReceived -= _onErrorFrameReceived;
            _errorFrameReceived = null;
        }
        _inner.BackgroundExceptionOccurred -= _onBackgroundException;
        _inner.FaultOccurred -= _onFault;
        _inner.Dispose();
    }

    private sealed class OptionsView(IBusRTOptionsConfigurator inner) : IBusRTOptionsConfigurator
    {
        public CanFeature Features => inner.Features;
        public Capability Capabilities { get; } = new(inner.Capabilities.Features,
            inner.Capabilities.Custom.Where(pair => pair.Key != "bus_usage_source")
                .Concat(new[] { new KeyValuePair<string, object?>("bus_usage_source", "software") })
                .ToDictionary(pair => pair.Key, pair => pair.Value));
        public int ChannelIndex => inner.ChannelIndex;
        public string? ChannelName => inner.ChannelName;
        public CanBusTiming BitTiming => inner.BitTiming;
        public TxRetryPolicy TxRetryPolicy => inner.TxRetryPolicy;
        public ChannelWorkMode WorkMode => inner.WorkMode;
        public bool InternalResistance => inner.InternalResistance;
        public CanProtocolMode ProtocolMode => inner.ProtocolMode;
        public ICanFilter Filter => inner.Filter;
        public CanFeature EnabledSoftwareFallback => inner.EnabledSoftwareFallback;
        public bool AllowErrorInfo => inner.AllowErrorInfo;
        public int AsyncBufferCapacity => inner.AsyncBufferCapacity;
        public IBufferAllocator BufferAllocator => inner.BufferAllocator;
        public CanExceptionPolicy? ExceptionPolicy { get => inner.ExceptionPolicy; set => inner.ExceptionPolicy = value; }
    }
}
