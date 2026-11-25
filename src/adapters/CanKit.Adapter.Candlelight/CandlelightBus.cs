using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Adapter.Candlelight.Native;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;

namespace CanKit.Adapter.Candlelight;

public sealed class CandlelightBus : ICanBus<CandlelightBusRtConfigurator>, IOwnership
{
    private readonly CandlelightDevice _device;
    private readonly byte _channel;
    private readonly ITransceiver _transceiver;
    private readonly BlockingCollection<CandleNative.Frame> _incoming;
    private readonly object _incomingGate = new();
    private readonly AsyncFramePipe<CanReceiveData> _receive;
    private readonly object _publishGate = new();
    private readonly CanExceptionPolicy _policy;
    private readonly CanBusExceptionDispatcher _exceptions;
    private Func<CanFrame, bool> _filter = _ => true;
    private IDisposable? _owner;
    private Exception? _incomingFault;
    private int _disposed;

    internal CandlelightBus(CandlelightDevice device, CandlelightBusOptions options, ITransceiver transceiver)
    {
        if (options.ChannelIndex < 0 || options.ChannelIndex > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options.ChannelIndex));
        if (options.ProtocolMode != CanProtocolMode.Can20)
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, options.Features);
        if (options.InternalResistance)
            throw new NotSupportedException("Candle.dll does not expose termination control.");
        if (options.WorkMode != ChannelWorkMode.Normal && options.WorkMode != ChannelWorkMode.ListenOnly && options.WorkMode != ChannelWorkMode.Echo)
            throw new NotSupportedException("Unsupported Candlelight work mode.");
        if (options.AsyncBufferCapacity <= 0)
            options.AsyncBufferCapacity = CandlelightBusOptions.DefaultAsyncBufferCapacity;
        _channel = (byte)options.ChannelIndex;
        Options = new CandlelightBusRtConfigurator();
        Options.Init(options);
        _transceiver = transceiver;
        _incoming = new BlockingCollection<CandleNative.Frame>(options.AsyncBufferCapacity);
        _receive = new AsyncFramePipe<CanReceiveData>(options.AsyncBufferCapacity,
            static item => item.CanFrame.Dispose());
        _policy = options.ExceptionPolicy ?? CanExceptionPolicy.Default;
        _exceptions = new CanBusExceptionDispatcher("Candlelight CAN bus", _policy,
            ex => BackgroundExceptionOccurred?.Invoke(this, ex),
            ex => FaultOccurred?.Invoke(this, ex),
            () => _incoming.CompleteAdding(),
            ex => _receive.ExceptionOccured(ex));
        ApplyConfig(options);
        _device = device;
        var started = false;
        try
        {
            _device.Start(_channel, this, options);
            started = true;
            _ = Task.Factory.StartNew(ReceiveLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch
        {
            if (started) _device.Stop(_channel, this);
            throw;
        }
    }

    public CandlelightBusRtConfigurator Options { get; }
    IBusRTOptionsConfigurator ICanBus.Options => Options;
    public BusNativeHandle NativeHandle => new(_device.Handle);
    public BusState BusState => BusState.Unknown;
    public event EventHandler<CanReceiveData>? FrameReceived;
    public event EventHandler<CanReceiveDataView>? FrameObserved;
    public event EventHandler<ICanErrorInfo>? ErrorFrameReceived;
    public event EventHandler<Exception>? BackgroundExceptionOccurred;
    public event EventHandler<Exception>? FaultOccurred;

    public void AttachOwner(IDisposable owner) => _owner = owner;

    public void ApplyConfig(ICanOptions options)
    {
        if (options is not CandlelightBusOptions candle)
            throw new ArgumentException("Expected Candlelight bus options.", nameof(options));
        var rules = candle.Filter.FilterRules.Concat(candle.Filter.SoftwareFilterRules).ToArray();
        foreach (var rule in rules)
        {
            var feature = rule is FilterRule.Range ? CanFeature.RangeFilter : CanFeature.MaskFilter;
            if ((candle.EnabledSoftwareFallback & feature) == 0)
                throw new CanFeatureNotSupportedException(feature, candle.Features);
        }
        Volatile.Write(ref _filter, FilterRule.Build(rules));
    }

    internal void Enqueue(CandleNative.Frame frame)
    {
        lock (_incomingGate)
        {
            try
            {
                if (Volatile.Read(ref _disposed) != 0 || _incoming.IsAddingCompleted) return;
                if (!_incoming.TryAdd(frame))
                {
                    _incoming.TryTake(out _);
                    _incoming.TryAdd(frame);
                }
            }
            catch (InvalidOperationException) { }
        }
    }

    internal void EnqueueFault(Exception ex)
    {
        lock (_incomingGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || _incoming.IsAddingCompleted) return;
            Volatile.Write(ref _incomingFault, ex);
            _incoming.CompleteAdding();
        }
    }

    private void ReceiveLoop()
    {
        foreach (var frame in _incoming.GetConsumingEnumerable())
        {
            if (Volatile.Read(ref _disposed) != 0) break;
            try { Deliver(frame); }
            catch (Exception ex) { _exceptions.Report(ex, CanExceptionSource.BackgroundLoop); }
        }
        var fault = Volatile.Read(ref _incomingFault);
        if (fault != null && Volatile.Read(ref _disposed) == 0)
            _exceptions.Report(fault, CanExceptionSource.BackgroundLoop, CanExceptionSeverity.Fault);
    }

    private void Deliver(CandleNative.Frame native)
    {
        if (native.EchoId != uint.MaxValue && Options.WorkMode != ChannelWorkMode.Echo) return;
        var frame = CandleFrameConverter.FromNative(native);
        if (frame.IsErrorFrame)
        {
            if (Options.AllowErrorInfo)
            {
                var info = new DefaultCanErrorInfo
                {
                    Type = (FrameErrorType)(native.CanId & 0x1FFFFFFFu),
                    RawErrorCode = native.CanId & 0x1FFFFFFFu,
                    SystemTimestamp = DateTime.UtcNow,
                    DeviceTimeSpan = TimeSpan.FromTicks((long)native.TimestampMicroseconds * 10),
                    Direction = FrameDirection.Rx,
                    Frame = frame
                };
                InvokeSubscriber(() => ErrorFrameReceived?.Invoke(this, info));
            }
            frame.Dispose();
            return;
        }
        if (!Volatile.Read(ref _filter)(frame)) { frame.Dispose(); return; }
        frame = CopyFrame(frame, Options.BufferAllocator);
        var data = new CanReceiveData(frame)
        {
            ReceiveTimestamp = TimeSpan.FromTicks((long)native.TimestampMicroseconds * 10),
            IsEcho = native.EchoId != uint.MaxValue
        };
        InvokeSubscriber(() => FrameReceived?.Invoke(this, data));
        if (Volatile.Read(ref _disposed) == 0)
            InvokeSubscriber(() => FrameObserved?.Invoke(this, new CanReceiveDataView(data)));
        lock (_publishGate)
        {
            if (_disposed == 0) { _receive.Publish(data); return; }
        }
        frame.Dispose();
    }

    private static CanFrame CopyFrame(in CanFrame source, IBufferAllocator allocator)
    {
        var owner = allocator.Rent(source.Len);
        try
        {
            source.Data.CopyTo(owner.Memory);
            return CanFrame.Classic(source.ID, owner,
                isExtendedFrame: source.IsExtendedFrame,
                isRemoteFrame: source.IsRemoteFrame,
                isErrorFrame: source.IsErrorFrame);
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    private void InvokeSubscriber(Action action)
    {
        try { action(); }
        catch (Exception ex) { _exceptions.Report(ex, CanExceptionSource.SubscriberCallback, _policy.SubscriberCallbackSeverity); }
    }

    internal void Send(in CanFrame frame)
    {
        ThrowIfDisposed();
        if (Options.WorkMode == ChannelWorkMode.ListenOnly)
            throw new InvalidOperationException("Cannot transmit in listen-only mode.");
        var native = CandleFrameConverter.ToNative(frame, _channel);
        _device.Send(_channel, ref native);
    }

    public int Transmit(IEnumerable<CanFrame> frames, int timeOut = 0) { ThrowIfDisposed(); return _transceiver.Transmit(this, frames); }
    public int Transmit(ReadOnlySpan<CanFrame> frames, int timeOut = 0) { ThrowIfDisposed(); return _transceiver.Transmit(this, frames); }
    public int Transmit(CanFrame[] frames, int timeOut = 0) => Transmit(frames.AsSpan(), timeOut);
    public int Transmit(ArraySegment<CanFrame> frames, int timeOut = 0) => Transmit(frames.AsSpan(), timeOut);
    public int Transmit(in CanFrame frame) { ThrowIfDisposed(); return _transceiver.Transmit(this, frame); }
    public Task<int> TransmitAsync(IEnumerable<CanFrame> frames, int timeOut = 0, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Transmit(frames, timeOut)); }
    public Task<int> TransmitAsync(CanFrame frame, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Transmit(frame)); }
    public IPeriodicTx TransmitPeriodic(CanFrame frame, PeriodicTxOptions options)
    {
        ThrowIfDisposed();
        if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) == 0)
            throw new CanFeatureNotSupportedException(CanFeature.CyclicTx, Options.Features);
        return SoftwarePeriodicTx.Start(this, frame, options);
    }
    public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0) => ReceiveAsync(count, timeOut).GetAwaiter().GetResult();
    public Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    { ThrowIfDisposed(); return _receive.ReceiveBatchAsync(count, timeOut, cancellationToken); }
    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await foreach (var frame in _receive.ReadAllAsync(cancellationToken)) yield return frame;
    }
    public void Reset() => throw new NotSupportedException("Candle.dll does not expose a channel reset operation.");
    public void ClearBuffer()
    {
        ThrowIfDisposed();
        lock (_publishGate)
        {
            while (_incoming.TryTake(out _)) { }
            ClearReceiveBuffer();
        }
    }

    private void ClearReceiveBuffer()
    {
        var frames = _receive.ReceiveBatchAsync(0, 0, CancellationToken.None).GetAwaiter().GetResult();
        foreach (var frame in frames) frame.CanFrame.Dispose();
    }
    public float BusUsage() => throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Features);
    public CanErrorCounters ErrorCounters() => throw new CanFeatureNotSupportedException(CanFeature.ErrorCounters, Options.Features);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _incoming.CompleteAdding();
        try { _device.Stop(_channel, this); }
        finally
        {
            lock (_publishGate) ClearReceiveBuffer();
            _receive.ExceptionOccured(new ObjectDisposedException(nameof(CandlelightBus)));
            Interlocked.Exchange(ref _owner, null)?.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(CandlelightBus));
    }
}
