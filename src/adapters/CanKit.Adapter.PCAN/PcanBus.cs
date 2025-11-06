using System.Runtime.InteropServices;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;
using Microsoft.Win32.SafeHandles;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN;

public sealed class PcanBus : ICanBus<PcanBusRtConfigurator>, IOwnership
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
    private readonly EventWaitHandle _recEvent;

    private Func<CanFrame, bool>? _softwareFilterPredicate;

    private bool _useSoftwareFilter;
    private readonly AsyncFramePipe<CanReceiveData> _asyncRx;

    internal PcanBus(IBusOptions options, ITransceiver transceiver)
    {
        Options = new PcanBusRtConfigurator();
        Options.Init((PcanBusOptions)options);
        _transceiver = transceiver;
        _asyncRx = new AsyncFramePipe<CanReceiveData>(Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null);

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

        ApplyConfigBeforeInit((PcanBusOptions)options);

        // Initialize according to selected protocol mode
        if (options.ProtocolMode == CanProtocolMode.CanFd)
        {

            CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.CanFd);

            var fd = PcanUtils.MapFdBitrate(options.BitTiming);
            var st = Api.Initialize(_handle, fd);
            if (st != PcanStatus.OK)
            {
                throw new CanBusCreationException($"PCAN InitializeFD failed: {st}");
            }
            CanKitLogger.LogInformation("PCAN: InitializeFD succeeded.");
        }
        else if (options.ProtocolMode == CanProtocolMode.Can20)
        {
            var baud = PcanUtils.MapClassicBaud(options.BitTiming);
            var st = Api.Initialize(_handle, baud);
            if (st != PcanStatus.OK)
            {
                throw new CanBusCreationException($"PCAN Initialize failed: {st}");
            }
            CanKitLogger.LogInformation("PCAN: Initialize (classic) succeeded.");
        }

        // Apply initial options
        ApplyConfig((PcanBusOptions)options);
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

        StartReceiveLoop();
    }


    public PcanStatus PCanState => Api.GetStatus(_handle);

    internal PcanChannel Handle => _handle;

    public BusNativeHandle NativeHandle { get; }

    public void AttachOwner(IDisposable owner)
    {
        _owner = owner;
    }

    private void ApplyConfigBeforeInit(PcanBusOptions pc)
    {
        if (pc.WorkMode == ChannelWorkMode.ListenOnly)
        {
            PcanUtils.ThrowIfError(Api.SetValue(_handle, PcanParameter.ListenOnly, ParameterValue.Activation.On),
                "SetValue(ListenOnly)", "PcanBus set ListenOnly workmode error");
        }
    }

    private void ApplyConfig(PcanBusOptions pc)
    {
        if (pc.WorkMode == ChannelWorkMode.Echo)
        {
            var result = Api.SetValue(_handle, PcanParameter.AllowEchoFrames, ParameterValue.Activation.On);
            if (result != PcanStatus.OK)
            {
                CanKitLogger.LogWarning("PCAN: set WokeMode=Echo failed");
                pc.WorkMode = ChannelWorkMode.Normal;
            }
        }

        var rules = pc.Filter.FilterRules;
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
                        pc.Filter.SoftwareFilterRules.Add(r);
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

    public IPeriodicTx TransmitPeriodic(CanFrame frame, PeriodicTxOptions options)
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
    public int Transmit(IEnumerable<CanFrame> frames, int _ = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames);
    }

    public int Transmit(ReadOnlySpan<CanFrame> frames, int _ = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames);
    }

    public int Transmit(CanFrame[] frames, int _ = 0)
        => Transmit(frames.AsSpan());

    public int Transmit(ArraySegment<CanFrame> frames, int _ = 0)
        => Transmit(frames.AsSpan());

    public int Transmit(in CanFrame frame)
        => _transceiver.Transmit(this, frame);

    //non-support time out
    public Task<int> TransmitAsync(IEnumerable<CanFrame> frames, int _ = 0, CancellationToken cancellationToken = default)
        => Task.FromResult(Transmit(frames));

    public Task<int> TransmitAsync(CanFrame frame, CancellationToken cancellationToken = default)
        => Task.FromResult(Transmit(frame));

    public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0)
    {
        return ReceiveAsync(count, timeOut).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return await _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken)
            .ConfigureAwait(false);
    }


    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await foreach (var item in _asyncRx.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

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
                _errorOccured += value;
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
                _errorOccured -= value;
            }
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


    private void StartReceiveLoop()
    {
        _pollCts = new CancellationTokenSource();

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
        try
        {
            _asyncRx.Clear();
            cts?.Cancel();
            _pollTask?.Wait(500);
        }
        catch { /* ignore on shutdown */ }
        finally
        {
            cts?.Dispose();
            CanKitLogger.LogDebug("PCAN: Poll loop stopped.");
        }
    }

    private void PollLoop(CancellationToken token)
    {
        var handles = new[] { _recEvent, token.WaitHandle };
        try
        {
            while (!token.IsCancellationRequested)
            {
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
                        PcanUtils.ToTransceiverStatus(span),
                        DateTime.Now,
                        (uint)raw,
                        rec.ReceiveTimestamp,
                        PcanUtils.ToDirection(span),
                        null,
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
                _asyncRx.Publish(rec);
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
}
