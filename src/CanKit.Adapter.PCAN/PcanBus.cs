using System.Runtime.InteropServices;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;
using Microsoft.Win32.SafeHandles;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN;

public sealed class PcanBus : ICanBus<PcanBusRtConfigurator>, IBusOwnership
{
    private readonly object _evtGate = new();

    private readonly PcanChannel _handle;

    private readonly ITransceiver _transceiver;
    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<ICanErrorInfo>? _errorOccured;

    private bool _isDisposed;

    private IDisposable? _owner;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private EventWaitHandle _recEvent;

    private Func<ICanFrame, bool>? _softwareFilterPredicate;


    private int _subscriberCount;
    private bool _useSoftwareFilter;
    private readonly AsyncFramePipe _asyncRx;
    private int _asyncConsumerCount;
    private CancellationTokenSource? _stopDelayCts;
    private int _asyncBufferingLinger;
    private int _rxLoopRunning;

    internal PcanBus(IBusOptions options, ITransceiver transceiver)
    {
        Options = new PcanBusRtConfigurator();
        Options.Init((PcanBusOptions)options);
        _transceiver = transceiver;
        _asyncRx = new AsyncFramePipe(Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null);

        _handle = PcanProvider.ParseHandle(Options.ChannelName!);
        options.Capabilities = PcanProvider.QueryCapabilities(_handle, Options.Features);
        options.Features = options.Capabilities.Features;

        NativeHandle = new BusNativeHandle((int)_handle);
        try
        {
            if (Api.GetValue(_handle, PcanParameter.ChannelCondition, out uint raw) == PcanStatus.OK)
            {
                var cond = (ChannelCondition)raw;
                if ((cond & ChannelCondition.ChannelAvailable) != ChannelCondition.ChannelAvailable)
                    throw new CanBusCreationException("PCAN handle is not available");
            }
            else
            {
                CanKitLogger.LogWarning("PCAN can't get channel condition for handle");
            }
        }
        catch (PcanBasicException)
        {
            throw new CanBusCreationException("PCAN handle is invalid");
        }

        CanKitLogger.LogInformation($"PCAN: Initializing on '{_handle}', Mode={options.ProtocolMode}, Features={Options.Features}");


        // Initialize according to selected protocol mode
        if (options.ProtocolMode == CanProtocolMode.CanFd)
        {

            CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.CanFd);

            var fd = MapFdBitrate(options.BitTiming);
            var st = Api.Initialize(_handle, fd);
            if (st != PcanStatus.OK)
            {
                throw new CanBusCreationException($"PCAN InitializeFD failed: {st}");
            }
            CanKitLogger.LogInformation("PCAN: InitializeFD succeeded.");
        }
        else if (options.ProtocolMode == CanProtocolMode.Can20)
        {
            var baud = MapClassicBaud(options.BitTiming);
            var st = Api.Initialize(_handle, baud);
            if (st != PcanStatus.OK)
            {
                throw new CanBusCreationException($"PCAN Initialize failed: {st}");
            }
            CanKitLogger.LogInformation("PCAN: Initialize (classic) succeeded.");
        }

        // Apply initial options
        ApplyConfig(options);
        CanKitLogger.LogDebug("PCAN: Initial options applied.");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _recEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        }
        else
        {
            var ok = Api.GetValue(_handle, PcanParameter.ReceiveEvent, out uint evHandle);
            if (ok != PcanStatus.OK)
                throw new InvalidOperationException($"Get ReceiveEvent failed: {ok}");

            _recEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            _recEvent.SafeWaitHandle?.Close();
            _recEvent.SafeWaitHandle = new SafeWaitHandle(new IntPtr(evHandle), false);
        }
    }


    public PcanStatus PCanState => Api.GetStatus(_handle);

    internal PcanChannel Handle => _handle;

    public BusNativeHandle NativeHandle { get; }

    public void AttachOwner(IDisposable owner)
    {
        _owner = owner;
    }

    public void ApplyConfig(ICanOptions options)
    {
        if (options is not PcanBusOptions pc)
            return;

        var rules = pc.Filter.filterRules;
        if (rules.Count > 0)
        {
            foreach (var r in rules)
            {
                if (r is FilterRule.Range rg)
                {
                    var mode = rg.FilterIdType == CanFilterIDType.Extend
                        ? FilterMode.Extended
                        : FilterMode.Standard;
                    PcanUtils.ThrowIfError(Api.FilterMessages(_handle, rg.From, rg.To, mode), "FilterMessages", "PcanBus set filers error");
                }
                else
                {
                    if (pc.SoftwareFilterEnabled)
                    {
                        pc.Filter.softwareFilter.Add(r);
                    }
                    else
                    {
                        throw new CanFilterConfigurationException("PCAN only supports range filters.");
                    }
                }
            }
            // Cache software filter predicate for polling loop
            _useSoftwareFilter = (Options.EnabledSoftwareFallback & CanFeature.MaskFilter) != 0
                                 && Options.Filter.SoftwareFilterRules.Count > 0;
            _softwareFilterPredicate = _useSoftwareFilter
                ? FilterRule.Build(Options.Filter.SoftwareFilterRules)
                : null;
        }

        if (pc.AllowErrorInfo)
        {
            PcanUtils.ThrowIfError(Api.SetValue(_handle, PcanParameter.AllowErrorFrames, ParameterValue.Activation.On),
                "SetAllowErrorFrames", "PcanBus enable error frames failed");
        }
    }

    public void Reset()
    {
        ThrowIfDisposed();
        PcanUtils.ThrowIfError(Api.Reset(_handle), "Reset", "Failed to reset PCAN handle");
        CanKitLogger.LogDebug("PCAN: Channel reset issued.");
    }

    public void ClearBuffer()
    {
        ThrowIfDisposed();
        // Reset clears the receive/transmit queues
        PcanUtils.ThrowIfError(Api.Reset(_handle), "Reset", "Failed to reset PCAN handle (in CleanBuffer)");
        CanKitLogger.LogDebug("PCAN: Buffers cleared via reset.");

    }

    public IPeriodicTx TransmitPeriodic(ICanFrame frame, PeriodicTxOptions options)
    {
        ThrowIfDisposed();
        if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) != 0)
            return SoftwarePeriodicTx.Start(this, frame, options);
        throw new CanFeatureNotSupportedException(CanFeature.CyclicTx, Options.Features);
    }

    public float BusUsage() => throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Features);

    public CanErrorCounters ErrorCounters()
        => throw new CanFeatureNotSupportedException(CanFeature.ErrorCounters, Options.Features);

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

    //non-support time out
    public Task<int> TransmitAsync(IEnumerable<ICanFrame> frames, int _ = 0, CancellationToken cancellationToken = default)
        => Task.FromResult(Transmit(frames));

    public Task<int> TransmitAsync(ICanFrame frame, CancellationToken cancellationToken = default)
        => Task.FromResult(Transmit(frame));

    public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0)
    {
        return ReceiveAsync(count, timeOut).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        Interlocked.Increment(ref _subscriberCount);
        Interlocked.Increment(ref _asyncConsumerCount);
        Volatile.Write(ref _asyncBufferingLinger, 0);
        StartReceiveLoopIfNeeded();

        try
        {
            return await _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            var remAsync = Interlocked.Decrement(ref _asyncConsumerCount);
            var rem = Interlocked.Decrement(ref _subscriberCount);
            if (rem == 0 && remAsync == 0)
            {
                Volatile.Write(ref _asyncBufferingLinger, 1);
                RequestStopReceiveLoop();
            }
        }
    }

#if NET8_0_OR_GREATER
    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _subscriberCount);
        Interlocked.Increment(ref _asyncConsumerCount);
        Volatile.Write(ref _asyncBufferingLinger, 0);
        StartReceiveLoopIfNeeded();
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
            if (rem == 0 && remAsync == 0)
            {
                Volatile.Write(ref _asyncBufferingLinger, 1);
                RequestStopReceiveLoop();
            }
        }
    }
#endif

    public PcanBusRtConfigurator Options { get; }

    IBusRTOptionsConfigurator ICanBus.Options => Options;


    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            StopReceiveLoop();
            _ = Api.Uninitialize(_handle);
        }
        finally
        {
            _isDisposed = true;
            _owner?.Dispose();
        }
    }

    public event EventHandler<CanReceiveData>? FrameReceived
    {
        add
        {
            lock (_evtGate)
            {
                var before = _frameReceived;
                _frameReceived += value;
                if (!ReferenceEquals(before, _frameReceived))
                {
                    Interlocked.Increment(ref _subscriberCount);
                    StartReceiveLoopIfNeeded();
                }
            }
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
            if (needStop) RequestStopReceiveLoop();
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
                var before = _errorOccured;
                _errorOccured += value;
                if (!ReferenceEquals(before, _errorOccured))
                {
                    Interlocked.Increment(ref _subscriberCount);
                    StartReceiveLoopIfNeeded();
                }
            }
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
                var before = _errorOccured;
                _errorOccured -= value;
                if (!ReferenceEquals(before, _errorOccured))
                {
                    var now = Interlocked.Decrement(ref _subscriberCount);
                    if (now == 0 && Volatile.Read(ref _asyncConsumerCount) == 0)
                        needStop = true;
                }
            }
            if (needStop) RequestStopReceiveLoop();
        }
    }

    public BusState BusState
    {
        get
        {
            ThrowIfDisposed();
            var state = Api.GetStatus(_handle);
            if ((state & PcanStatus.BusOff) != 0)
                return BusState.BusOff;
            if ((state & PcanStatus.BusPassive) != 0)
                return BusState.ErrPassive;
            if ((state & PcanStatus.BusWarning) != 0)
                return BusState.ErrWarning;
            return BusState.None;
        }

    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new CanBusDisposedException();
    }


    private void StartReceiveLoopIfNeeded()
    {
        if (Interlocked.Exchange(ref _rxLoopRunning, 1) == 1) return;
        _pollCts = new CancellationTokenSource();
        DrainReceive();
        var token = _pollCts.Token;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var h = (uint)_recEvent.SafeWaitHandle.DangerousGetHandle().ToInt32();
            PcanUtils.ThrowIfError(Api.SetValue(_handle, PcanParameter.ReceiveEvent, h), "SetValue(ReceiveEvent)",
                "Start PcanBus receive loop failed");
        }
        _pollTask = Task.Run(() => PollLoop(token), token);
        CanKitLogger.LogDebug("PCAN: Poll loop started.");
    }
    private void StopReceiveLoop()
    {
        var task = Volatile.Read(ref _pollTask);
        var cts = Volatile.Read(ref _pollCts);

        if (Interlocked.Exchange(ref _rxLoopRunning, 0) == 0)
            return;
        try
        {
            _asyncRx.Clear();
            cts?.Cancel();
            _pollTask?.Wait(500);
        }
        catch { /* ignore on shutdown */ }
        finally
        {
            Interlocked.CompareExchange(ref _pollTask, task, null);
            Interlocked.CompareExchange(ref _pollCts, cts, null);
            cts?.Dispose();
            Volatile.Write(ref _asyncBufferingLinger, 0);
            CanKitLogger.LogDebug("PCAN: Poll loop stopped.");
        }
    }

    private void RequestStopReceiveLoop()
    {
        var delay = Options.ReceiveLoopStopDelayMs;
        if (delay <= 0)
        {
            StopReceiveLoop();
            return;
        }
        _stopDelayCts?.Cancel();
        var cts = new CancellationTokenSource();
        _stopDelayCts = cts;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                if (Volatile.Read(ref _subscriberCount) == 0 && Volatile.Read(ref _asyncConsumerCount) == 0)
                {
                    StopReceiveLoop();
                }
            }
            catch { /*ignore*/ }
        }, cts.Token);
    }

    private void PollLoop(CancellationToken token)
    {
        var handles = new[] { _recEvent, token.WaitHandle };
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Volatile.Read(ref _rxLoopRunning) == 0)
                {
                    break;
                }

                var signaled = WaitHandle.WaitAny(handles);
                if (signaled == 1)
                {
                    break;
                }
                if (signaled != 0)
                {
                    continue;
                }

                DrainReceive();
            }
        }
        catch (Exception ex)
        {
            HandleBackgroundException(ex);
        }
    }


    private void DrainReceive()
    {
        while (true)
        {
            bool any = false;
            foreach (var rec in _transceiver.Receive(this, 64))
            {
                any = true;
                if (rec.CanFrame.IsErrorFrame)
                {
                    var raw = rec.CanFrame.ID;
                    var span = rec.CanFrame.Data.Span;
                    var info = new DefaultCanErrorInfo(
                        PcanUtils.ToFrameErrorType(raw, span),
                        CanKitExtension.ToControllerStatus(span[2], span[3]),
                        PcanUtils.ToProtocolViolationType(raw, span),
                        PcanUtils.ToErrorLocation(span),
                        DateTime.Now,
                        (uint)raw,
                        rec.ReceiveTimestamp,
                        PcanUtils.ToDirection(span),
                        null,
                        PcanUtils.ToTransceiverStatus(span),
                        PcanUtils.ToErrorCounters(span),
                        rec.CanFrame);
                    _errorOccured?.Invoke(this, info);
                    continue;
                }

                var pred = _softwareFilterPredicate;
                if (_useSoftwareFilter && pred is not null && !pred(rec.CanFrame))
                {
                    continue;
                }

                _frameReceived?.Invoke(this, rec);
                if (Volatile.Read(ref _asyncConsumerCount) > 0 || Volatile.Read(ref _asyncBufferingLinger) == 1)
                {
                    _asyncRx.Publish(rec);
                }
            }

            if (!any) break;
        }
    }

    public event EventHandler<Exception>? BackgroundExceptionOccurred;

    private void HandleBackgroundException(Exception ex)
    {
        try { CanKitLogger.LogError("PCAN bus occured background exception.", ex); } catch { }
        try { _asyncRx.ExceptionOccured(ex); } catch { }
        try { var snap = Volatile.Read(ref BackgroundExceptionOccurred); snap?.Invoke(this, ex); } catch { }
    }

    private static Bitrate MapClassicBaud(CanBusTiming timing)
    {
        if (!timing.Classic!.Value.Nominal.IsTarget)
        {
            throw new CanBusConfigurationException(
                "Classic timing must specify a target nominal bitrate.");
        }

        var b = timing.Classic.Value.Nominal.Bitrate!.Value;
        return b switch
        {
            1_000_000 => Bitrate.Pcan1000,
            800_000 => Bitrate.Pcan800,
            500_000 => Bitrate.Pcan500,
            250_000 => Bitrate.Pcan250,
            125_000 => Bitrate.Pcan125,
            100_000 => Bitrate.Pcan100,
            83_000 => Bitrate.Pcan83,
            95_000 => Bitrate.Pcan95,
            50_000 => Bitrate.Pcan50,
            47_000 => Bitrate.Pcan47,
            33_000 => Bitrate.Pcan33,
            20_000 => Bitrate.Pcan20,
            10_000 => Bitrate.Pcan10,
            5_000 => Bitrate.Pcan5,
            _ => throw new CanBusConfigurationException($"Unsupported PCAN classic bitrate: {b}")
        };
    }

    private static BitrateFD MapFdBitrate(CanBusTiming timing)
    {

        var (nomial, data, clockTmp) = timing.Fd!.Value;
        var clock = clockTmp ?? 80;
        // If advanced override is provided, build a custom FD string using same segments for both phases.
        BitrateFD.BitrateSegment nominalSeg = new BitrateFD.BitrateSegment();
        BitrateFD.BitrateSegment dataSeg = new BitrateFD.BitrateSegment();

        if (!Enum.IsDefined(typeof(BitrateFD.ClockFrequency), clock * 1_000_000))
        {
            throw new CanBusConfigurationException(
                $"Unsupported PCAN FD clock frequency: {clock} MHz.");
        }

        if (nomial.Segments is { } seg)
        {
            nominalSeg.Tseg1 = seg.Tseg1;
            nominalSeg.Tseg2 = seg.Tseg2;
            nominalSeg.Brp = seg.Brp;
            nominalSeg.Mode = BitrateFD.BitrateType.ArbitrationPhase;
            nominalSeg.Sjw = seg.Sjw;

        }
        else
        {
            var bit = timing.Fd.Value.Nominal.Bitrate!.Value;
            var samplePoint = timing.Fd.Value.Nominal.SamplePointPermille ?? 800;
            var segment = BitTimingSolver.FromSamplePoint(clock, bit, samplePoint/1000.0);
            nominalSeg.Tseg1 = segment.Tseg1;
            nominalSeg.Tseg2 = segment.Tseg2;
            nominalSeg.Brp = segment.Brp;
            nominalSeg.Mode = BitrateFD.BitrateType.ArbitrationPhase;
            nominalSeg.Sjw = segment.Sjw;
        }

        if (data.Segments is { } seg1)
        {
            dataSeg.Tseg1 = seg1.Tseg1;
            dataSeg.Tseg2 = seg1.Tseg2;
            dataSeg.Brp = seg1.Brp;
            dataSeg.Mode = BitrateFD.BitrateType.DataPhase;
            dataSeg.Sjw = seg1.Sjw;
        }
        else
        {
            var bit = timing.Fd.Value.Nominal.Bitrate!.Value;
            var samplePoint = timing.Fd.Value.Nominal.SamplePointPermille ?? 800;
            var segment = BitTimingSolver.FromSamplePoint(clock, bit, samplePoint/1000.0);
            dataSeg.Tseg1 = segment.Tseg1;
            dataSeg.Tseg2 = segment.Tseg2;
            dataSeg.Brp = segment.Brp;
            dataSeg.Mode = BitrateFD.BitrateType.ArbitrationPhase;
            dataSeg.Sjw = segment.Sjw;
        }
        return new BitrateFD((BitrateFD.ClockFrequency)(clock * 1_000_000), nominalSeg, dataSeg);
    }
}
