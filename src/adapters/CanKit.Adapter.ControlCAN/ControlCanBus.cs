using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.ControlCAN.Definitions;
using CanKit.Adapter.ControlCAN.Options;
using CcApi = CanKit.Adapter.ControlCAN.Native.ControlCAN;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;
using CanKit.Adapter.ControlCAN.Diagnostics;
using CanKit.Adapter.ControlCAN.Utils;

namespace CanKit.Adapter.ControlCAN;

public sealed class ControlCanBus : ICanBus<ControlCanBusRtConfigurator>, IOwnership
{
    private const int MaxReceiveBatchesPerPoll = 64;
    private readonly object _evtGate = new();
    private readonly object _lifecycleGate = new();
    private readonly object _receiveGate = new();
    private readonly ITransceiver _transceiver;
    private readonly ControlCanDevice _device;
    private readonly ControlCanRangeFilterTable _rangeFilters;
    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<CanReceiveDataView>? _frameObserved;
    private EventHandler<ICanErrorInfo>? _errorOccurred;
    private int _disposeRequested;
    private int _cleanupStarted;
    private int _activeNativeOperations;
    private bool _receiveStoppedForCleanup;
    private bool _channelInitialized;
    private bool _nativeCleanupCompleted;
    private int _receiveGeneration;
    private IDisposable? _owner;
    private Exception? _startupFailure;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private readonly AsyncFramePipe<CanReceiveData> _asyncRx;
    private Func<CanFrame, bool>? _softwareFilterPredicate;

    private readonly HashSet<int> _autoSendIndexes = new();
    private readonly Dictionary<int, CcApi.VCI_AUTO_SEND_OBJ> _autoSendEntries = new();
    private readonly object _autoSendGate = new();

    private readonly ControlCanDeviceKind _devType;
    private readonly uint _rawDevType;
    private readonly uint _devIndex;
    private readonly CanExceptionPolicy _exceptionPolicy;
    private readonly CanBusExceptionDispatcher _exceptions;

    internal ControlCanBus(ControlCanDevice device, IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
    {
        ControlCanPlatformGuard.EnsureSupported();
        Options = new ControlCanBusRtConfigurator();
        Options.Init((ControlCanBusOptions)options);
        _device = device;
        _transceiver = transceiver;
        _asyncRx = new AsyncFramePipe<CanReceiveData>(Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null,
            static data => data.CanFrame.Dispose());

        var dt = (ControlCanDeviceType)device.Options.DeviceType;
        _rawDevType = (uint)dt.Code;
        _devType = (ControlCanDeviceKind)dt.Code;
        _devIndex = device.Options.DeviceIndex;

        // Reflect capabilities from provider
        if (provider is ICanCapabilityProvider sniffer)
            options.Capabilities = sniffer.QueryCapabilities(options);
        else
            options.Capabilities = new Capability(provider.StaticFeatures);
        options.Features = options.Capabilities.Features;

        _exceptionPolicy = options.ExceptionPolicy ?? CanExceptionPolicy.Default;
        _exceptions = new CanBusExceptionDispatcher(
            "Control CAN bus",
            _exceptionPolicy,
            raiseBackground: ex =>
            {
                try { var snap = Volatile.Read(ref BackgroundExceptionOccurred); snap?.Invoke(this, ex); } catch { /*ignore*/ }
            },
            raiseFault: ex =>
            {
                Interlocked.CompareExchange(ref _startupFailure, ex, null);
                try { var snap = Volatile.Read(ref FaultOccurred); snap?.Invoke(this, ex); } catch { /*ignore*/  }
            },
            stopBackground: () =>
            {
                Dispose();
            },
            failAsyncReceivers: ex =>
            {
                try { _asyncRx.ExceptionOccured(ex); } catch { /*ignore*/ }
            });

        // Build and apply initial configuration
        if (options.ProtocolMode != CanProtocolMode.Can20)
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, options.Features);

        // Build and validate the complete filter snapshot before any native channel changes.
        _rangeFilters = new ControlCanRangeFilterTable(this, device);
        var filterPlan = ControlCanFilterPlanner.Prepare(options, _rangeFilters.IsSupported);
        var (t0, t1) = MapBaudRate(options.BitTiming);
        var cfg = new CcApi.VCI_INIT_CONFIG
        {
            AccCode = filterPlan.AccCode,
            AccMask = filterPlan.AccMask,
            // Filter selects single/dual acceptance mode, not the CAN ID format.
            Filter = filterPlan.Mode == ControlCanFilterMode.HardwareMask ? (byte)1 : (byte)0,
            Timing0 = t0,
            Timing1 = t1,
            Mode = options.WorkMode == ChannelWorkMode.ListenOnly ? (byte)1 : (byte)0,
        };

        CanKitLogger.LogInformation($"ControlCAN: Initializing dev={_devType}, idx={((ControlCanBusOptions)options).ChannelIndex} ch={options.ChannelIndex}, baud={options.BitTiming.Classic?.Nominal.Bitrate}");
        _device.AcquireChannel(CanIndex);
        try
        {
            ApplyConfig(options);
            // Device already opened by ControlCanDevice; just init channel
            if (CcApi.VCI_InitCAN(_rawDevType, _devIndex, (uint)Options.ChannelIndex, ref cfg) == 0)
                throw new CanBusCreationException("VCI_InitCAN failed.");
            _channelInitialized = true;
            Reset();
            ApplyFilterPlan(filterPlan);
            if (CcApi.VCI_StartCAN(_rawDevType, _devIndex, (uint)Options.ChannelIndex) == 0)
                throw new CanBusCreationException("VCI_StartCAN failed.");

            NativeHandle = new BusNativeHandle((nint)((_rawDevType << 24) | (_devIndex << 16) | (uint)Options.ChannelIndex));
            StartReceiveLoop();
        }
        catch (Exception ex)
        {
            CleanupChannel();
            if (NativeLibraryLoad.IsFailure(ex))
                throw ControlCanException.NativeLibraryNotFound("Open", ex);
            throw;
        }
    }

    public BusNativeHandle NativeHandle { get; }

    public ControlCanBusRtConfigurator Options { get; }
    IBusRTOptionsConfigurator ICanBus.Options => Options;

    public void AttachOwner(IDisposable owner)
    {
        bool completed;
        bool stopped;
        lock (_lifecycleGate)
        {
            completed = _nativeCleanupCompleted;
            if (!completed) _owner = owner;
            stopped = Volatile.Read(ref _disposeRequested) != 0 || _exceptions.IsFaulted;
        }
        // An immediate receive fault can finish cleanup before the factory attaches its lease.
        if (completed) TryCleanup("release device lease after early receive failure", owner.Dispose);
        if (stopped)
            throw new CanBusCreationException("ControlCAN channel stopped while opening: " +
                (Volatile.Read(ref _startupFailure)?.Message ?? "the channel was disposed before ownership was attached."));
    }

    public void ApplyConfig(IBusOptions options)
    {
        using var operation = EnterNativeOperation();
        var bitRate = options.BitTiming.Classic?.Nominal.Bitrate ??
                      throw new CanBusConfigurationException("Classic timing must specify a target nominal bitrate.");
        unsafe
        {
            if (_devType is ControlCanDeviceKind.VCI_USBCAN_E_U or ControlCanDeviceKind.VCI_USBCAN_2E_U)
            {
                var value = bitRate switch
                {
                    1_000_000 => 0x060003U,
                    800_000 => 0x060004U,
                    500_000 => 0x060007U,
                    250_000 => 0x1C0008U,
                    125_000 => 0x1C0011U,
                    100_000 => 0x160023U,
                    50_000 => 0x1C002CU,
                    20_000 => 0x1600B3U,
                    10_000 => 0x1C00E0U,
                    5_000 => 0x1C01C1U,
                    _ => throw new CanBusConfigurationException($"Unsupported ControlCAN classic bitrate: {bitRate}")
                };
                CcApi.VCI_SetReference(_rawDevType, _devIndex, CanIndex, 0, &value);
            }
            else if (_devType is ControlCanDeviceKind.VCI_USBCAN_4E_U)
            {
                //Only for valid
                _ = bitRate switch
                {
                    1_000_000 => 0x060003U,
                    800_000 => 0x060004U,
                    500_000 => 0x060007U,
                    250_000 => 0x1C0008U,
                    125_000 => 0x1C0011U,
                    100_000 => 0x160023U,
                    50_000 => 0x1C002CU,
                    20_000 => 0x1600B3U,
                    10_000 => 0x1C00E0U,
                    5_000 => 0x1C01C1U,
                    _ => throw new CanBusConfigurationException($"Unsupported ControlCAN classic bitrate: {bitRate}")
                };
                CcApi.VCI_SetReference(_rawDevType, _devIndex, CanIndex, 0, &bitRate);
            }
        }

    }
    /// <summary>
    /// Filters are fixed when the channel is opened. Close and reopen the channel to
    /// change them; replacing native tables on a running channel is not supported.
    /// </summary>
    /// <exception cref="CanBusConfigurationException">The channel has already been initialized.</exception>
    public void ApplyConfigAfterInit(IBusOptions options)
    {
        ThrowIfDisposed();
        throw new CanBusConfigurationException(
            "ControlCAN filters cannot be changed on an initialized bus. Close and reopen the channel with the new filters.");
    }

    private void ApplyFilterPlan(ControlCanFilterPlan plan)
    {
        _rangeFilters.Clear();
        if (plan.Mode == ControlCanFilterMode.HardwareRanges)
        {
            try
            {
                _rangeFilters.Program(plan.Rules.Cast<FilterRule.Range>());
            }
            catch
            {
                // Keep the dirty marker if cleanup fails, so reopening cannot silently
                // inherit a stale table. The original programming error remains the cause.
                TryCleanup("clear range filters after programming failure", _rangeFilters.Clear);
                throw;
            }
        }

        Volatile.Write(ref _softwareFilterPredicate, plan.SoftwarePredicate);
        if (plan.Mode == ControlCanFilterMode.Software)
        {
            CanKitLogger.LogInformation($"ControlCAN: Evaluating {plan.Rules.Length} filter rules in software; " +
                                        "the hardware filter is open, hardware range filtering is unused, " +
                                        "and all bus traffic reaches the receive queue.");
        }
    }

    public void Reset()
    {
        using var operation = EnterNativeOperation();
        ControlCanErr.ThrowIfErr(CcApi.VCI_ResetCAN(_rawDevType, _devIndex, CanIndex), "VCI_ResetCAN()", this);
    }

    public void ClearBuffer()
    {
        using var operation = EnterNativeOperation();
        lock (_receiveGate)
        {
            ThrowIfDisposed();
            ControlCanErr.ThrowIfErr(CcApi.VCI_ClearBuffer(_rawDevType, _devIndex, CanIndex), "VCI_ClearBuffer()", this);
            // Invalidate frames already held by the receive iterator as well as queued frames.
            unchecked { _receiveGeneration++; }
            DiscardQueuedFrames();
        }
    }

    public int Transmit(IEnumerable<CanFrame> frames, int timeOut = 0)
    {
        using var operation = EnterNativeOperation();
        return _transceiver.Transmit(this, frames);
    }

    public int Transmit(ReadOnlySpan<CanFrame> frames, int timeOut = 0)
    {
        using var operation = EnterNativeOperation();
        return _transceiver.Transmit(this, frames);
    }

    public int Transmit(CanFrame[] frames, int timeOut = 0) => Transmit(frames.AsSpan(), timeOut);
    public int Transmit(ArraySegment<CanFrame> frames, int timeOut = 0) => Transmit(frames.AsSpan(), timeOut);
    public int Transmit(in CanFrame frame)
    {
        using var operation = EnterNativeOperation();
        return _transceiver.Transmit(this, frame);
    }

    public IPeriodicTx TransmitPeriodic(CanFrame frame, PeriodicTxOptions options)
    {
        ThrowIfDisposed();
        if ((Options.Features & CanFeature.CyclicTx) != 0)
        {
            if (GetAutoSendIndex(false) < 32)
            {
                return new ControlCanPeriodicTx(this, frame, options);
            }
            if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) == 0)
            {
                throw new CanBusException(CanKitErrorCode.FeatureNotSupported,
                    "Control Can Bus only supported 32 set of filters.");
            }
        }
        if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) != 0)
            return SoftwarePeriodicTx.Start(this, frame, options);
        throw new CanFeatureNotSupportedException(CanFeature.CyclicTx, Options.Features);
    }

    public Task<int> TransmitAsync(IEnumerable<CanFrame> frames, int _ = 0, CancellationToken cancellationToken = default)
        => Task.FromResult(Transmit(frames));

    public Task<int> TransmitAsync(CanFrame frame, CancellationToken cancellationToken = default)
        => Task.FromResult(Transmit(frame));

    public float BusUsage() => throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Features);
    public CanErrorCounters ErrorCounters()
    {
        using var operation = EnterNativeOperation();
        if (CcApi.VCI_ReadErrInfo(_rawDevType, _devIndex, (uint)Options.ChannelIndex, out var err) == 0)
            return default;

        var rec = err.Passive_ErrData.Length >= 2 ? err.Passive_ErrData[1] : (byte)0;
        var tec = err.Passive_ErrData.Length >= 3 ? err.Passive_ErrData[2] : (byte)0;
        return new CanErrorCounters
        {
            TransmitErrorCounter = tec,
            ReceiveErrorCounter = rec
        };
    }

    public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0)
        => ReceiveAsync(count, timeOut).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return await _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await foreach (var item in _asyncRx.ReadAllAsync(cancellationToken))
            yield return item;
    }

    public BusState BusState => BusState.Unknown;

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

    public event EventHandler<CanReceiveDataView>? FrameObserved
    {
        add
        {
            lock (_evtGate)
            {
                _frameObserved += value;
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _frameObserved -= value;
            }
        }
    }

    public event EventHandler<ICanErrorInfo>? ErrorFrameReceived
    {
        add
        {
            if (!Options.AllowErrorInfo)
                throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
            lock (_evtGate)
            {
                _errorOccurred += value;
            }
        }
        remove
        {
            if (!Options.AllowErrorInfo)
                throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
            lock (_evtGate)
            {
                _errorOccurred -= value;
            }
        }
    }

    public event EventHandler<Exception>? BackgroundExceptionOccurred;
    public event EventHandler<Exception>? FaultOccurred;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0) return;
        CleanupChannel();
    }

    private void CleanupChannel()
    {
        var pendingReceive = StopReceiveLoop();
        if (pendingReceive == null)
            FinishCleanupChannel();
        else
        {
            // A slow callback may outlive the wait, and a receive callback cannot join
            // its own task. Keep the reservation and lease until the task actually exits.
            // Use the captured task even if another cleanup has cleared the shared field.
            _ = pendingReceive.ContinueWith(_ => FinishCleanupChannel(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private void FinishCleanupChannel()
    {
        lock (_lifecycleGate)
        {
            _receiveStoppedForCleanup = true;
            // A transmit may still be inside the driver or enumerating caller-owned frames.
            // Its scope will retry cleanup when the last active native operation finishes.
            if (_activeNativeOperations != 0 || Interlocked.Exchange(ref _cleanupStarted, 1) != 0) return;
        }
        try
        {
            var cts = Interlocked.Exchange(ref _pollCts, null);
            if (cts != null) TryCleanup("dispose receive cancellation", cts.Dispose);
            lock (_receiveGate)
            {
                TryCleanup("clear receive queue", DiscardQueuedFrames);
                if (_channelInitialized)
                {
                    DisableAutoSendEntries();
                    TryCleanup("reset channel", () => ControlCanErr.ThrowIfErr(
                        CcApi.VCI_ResetCAN(_rawDevType, _devIndex, CanIndex), "VCI_ResetCAN()", this));
                    TryCleanup("clear range filters; the next open must retry if this fails", _rangeFilters.Clear);
                }
            }
            TryCleanup("log receive shutdown", () => CanKitLogger.LogDebug("ControlCAN: Poll loop stopped."));
        }
        finally
        {
            IDisposable? owner;
            lock (_lifecycleGate)
            {
                _nativeCleanupCompleted = true;
                owner = _owner;
                _owner = null;
            }
            if (owner != null) TryCleanup("release device lease", owner.Dispose);
            _device.ReleaseChannel(CanIndex);
            // Mark cleanup complete only after releasing native state and the device lease.
            _ = Interlocked.Exchange(ref _pollTask, null);
        }
    }

    private void TryCleanup(string operation, Action cleanup)
    {
        try { cleanup(); }
        catch (Exception ex)
        {
            LogCleanupFailure(operation, ex);
        }
    }

    private void DiscardQueuedFrames()
    {
        _asyncRx.Clear(static frame => frame.CanFrame.Dispose());
    }

    private void LogCleanupFailure(string operation, Exception ex)
    {
        try
        {
            CanKitLogger.LogWarning(
                $"ControlCAN: Failed to {operation} during cleanup (device={_devType}, index={_devIndex}, channel={CanIndex}).", ex);
        }
        catch { /* A logger failure must not hide the original error or skip cleanup. */ }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeRequested) != 0) throw new CanBusDisposedException();
    }

    internal NativeOperation EnterNativeOperation()
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            _activeNativeOperations++;
            return new NativeOperation(this);
        }
    }

    private void ExitNativeOperation()
    {
        bool finishCleanup;
        lock (_lifecycleGate)
        {
            _activeNativeOperations--;
            finishCleanup = _activeNativeOperations == 0 && _receiveStoppedForCleanup;
        }
        if (finishCleanup) FinishCleanupChannel();
    }

    // Use only as a local using scope: native operations retain the channel, but do not
    // hold a monitor while invoking a driver, transceiver or caller-supplied enumerable.
    internal readonly struct NativeOperation(ControlCanBus bus) : IDisposable
    {
        public void Dispose() => bus.ExitNativeOperation();
    }

    private void StartReceiveLoop()
    {
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        // Run user-supplied logging before scheduling work: a constructor failure must
        // not leave a receive task behind before the caller can attach the device lease.
        CanKitLogger.LogDebug("ControlCAN: Starting poll loop.");
        lock (_lifecycleGate)
        {
            _pollTask = Task.Run(() =>
            {
                // This acquire pairs with the assignment under the outer lock. It keeps
                // the task from faulting before _pollTask is published; a Volatile.Write
                // after Task.Run alone would not prevent that early execution.
                lock (_lifecycleGate) { }
                PollLoop(token);
            }, token);
        }
    }

    private Task? StopReceiveLoop()
    {
        var task = Volatile.Read(ref _pollTask);
        var cts = Volatile.Read(ref _pollCts);
        if (cts != null) TryCleanup("cancel receive loop", cts.Cancel);
        if (task == null) return null;
        if (Task.CurrentId == task.Id) return task;
        // A timeout only defers cleanup; it never authorizes resetting or closing a
        // channel whose callback or native receive is still running.
        try
        {
            if (task.Wait(500)) return null;
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException)) { }
        catch (Exception ex) { LogCleanupFailure("join receive loop", ex); }
        return task.IsCompleted ? null : task;
    }

    private void PollLoop(CancellationToken token)
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var receiveBudgetExhausted = DrainReceive(token);
                token.ThrowIfCancellationRequested();
                var errSnap = Volatile.Read(ref _errorOccurred);
                if (errSnap != null && Options.AllowErrorInfo && ReadErrorInfo(out var info) && info is not null)
                {
                    try
                    {
                        errSnap.Invoke(this, info);
                    }
                    catch (Exception e)
                    {
                        _exceptions.Report(
                            e,
                            CanExceptionSource.SubscriberCallback,
                            severity: _exceptionPolicy.SubscriberCallbackSeverity,
                            message: "ControlCAN ErrorFrameReceived handler threw an exception.");
                    }
                }
                if (receiveBudgetExhausted)
                    Thread.Yield();
                else
                    PreciseDelay.Delay(TimeSpan.FromMilliseconds(Math.Max(1, Options.PollingInterval)), ct: token);
            }
        }
        catch (OperationCanceledException ex)
        {
            _exceptions.Report(
                ex,
                CanExceptionSource.BackgroundLoop,
                message: "ControlCAN poll loop canceled.");
        }
        catch (Exception ex)
        {
            // 非预期异常：记录日志，通知故障并退出循环
            _exceptions.Report(
                ex,
                CanExceptionSource.BackgroundLoop,
                severity: CanExceptionSeverity.Fault,
                message: "ControlCAN poll loop faulted.");
        }
    }

    private bool DrainReceive(CancellationToken token)
    {
        // Bound each poll even if traffic continuously refills the native queue. Error
        // reporting must still run, but reaching the budget must not impose the idle delay.
        for (var batch = 0; batch < MaxReceiveBatchesPerPoll; batch++)
        {
            token.ThrowIfCancellationRequested();
            var predicate = Volatile.Read(ref _softwareFilterPredicate);
            var generation = Volatile.Read(ref _receiveGeneration);
            bool anyReceived = false;
            using var frames = _transceiver.Receive(this, CcApi.BATCH_COUNT).GetEnumerator();
            while (!token.IsCancellationRequested)
            {
                CanReceiveData frame;
                lock (_receiveGate)
                {
                    if (generation != _receiveGeneration || token.IsCancellationRequested || !frames.MoveNext())
                    {
                        break;
                    }
                    frame = frames.Current;
                }
                anyReceived = true;
                if (token.IsCancellationRequested || generation != Volatile.Read(ref _receiveGeneration))
                {
                    frame.CanFrame.Dispose();
                    break;
                }
                bool accepted;
                try
                {
                    accepted = predicate == null || predicate(frame.CanFrame);
                }
                catch (Exception ex)
                {
                    frame.CanFrame.Dispose();
                    _exceptions.Report(ex, CanExceptionSource.SubscriberCallback,
                        severity: _exceptionPolicy.SubscriberCallbackSeverity,
                        message: "ControlCAN software filter predicate threw an exception; frame discarded.");
                    continue;
                }
                if (!accepted || token.IsCancellationRequested || generation != Volatile.Read(ref _receiveGeneration))
                {
                    frame.CanFrame.Dispose();
                    continue;
                }
                // Callbacks borrow the frame synchronously before queue publication.
                // ClearBuffer or disposal can suppress publication after a callback has
                // already observed it; release the frame only after that callback returns.
                try
                {
                    var evSnap = Volatile.Read(ref _frameReceived);
                    evSnap?.Invoke(this, frame);
                }
                catch (Exception e)
                {
                    _exceptions.Report(
                        e,
                        CanExceptionSource.SubscriberCallback,
                        severity: _exceptionPolicy.SubscriberCallbackSeverity,
                        message: "ControlCAN FrameReceived handler threw an exception.");
                }

                if (token.IsCancellationRequested || generation != Volatile.Read(ref _receiveGeneration))
                {
                    frame.CanFrame.Dispose();
                    break;
                }
                try
                {
                    var evSnap = Volatile.Read(ref _frameObserved);
                    evSnap?.Invoke(this, new CanReceiveDataView(frame));
                }
                catch (Exception e)
                {
                    _exceptions.Report(
                        e,
                        CanExceptionSource.SubscriberCallback,
                        severity: _exceptionPolicy.SubscriberCallbackSeverity,
                        message: "ControlCAN FrameObserved handler threw an exception.");
                }

                lock (_receiveGate)
                {
                    if (token.IsCancellationRequested || generation != _receiveGeneration)
                        frame.CanFrame.Dispose();
                    else
                        _asyncRx.Publish(frame);
                }
            }
            if (!anyReceived && generation == Volatile.Read(ref _receiveGeneration)) return false;
        }
        return true;
    }

    private static (byte t0, byte t1) MapBaudRate(CanBusTiming timing)
    {
        if (!timing.Classic.HasValue || !timing.Classic.Value.Nominal.IsTarget)
            throw new CanBusConfigurationException("Classic timing must specify a target nominal bitrate.");
        var bps = timing.Classic.Value.Nominal.Bitrate!.Value;
        // Typical SJA1000 @ 8MHz timing table
        return bps switch
        {
            1_000_000 => (0x00, 0x14),
            800_000 => (0x00, 0x16),
            666_000 => (0x80, 0xB6),
            500_000 => (0x00, 0x1c),
            400_000 => (0x80, 0xFA),
            250_000 => (0x01, 0x1c),
            200_000 => (0x81, 0xFA),
            125_000 => (0x03, 0x1c),
            100_000 => (0x04, 0x1c),
            80_000 => (0x83, 0Xff),
            50_000 => (0x09, 0x1c),
            40_000 => (0x87, 0xFF),
            20_000 => (0x18, 0x1C),
            10_000 => (0x31, 0x1C),
            5_000 => (0xBF, 0xFF),
            _ => throw new CanBusConfigurationException($"Unsupported ControlCAN classic bitrate: {bps}")
        };
    }

    internal int GetAutoSendIndex(bool add = true)
    {
        ThrowIfDisposed();
        int x = 0;
        lock (_autoSendGate)
        {
            while (true)
            {
                if (!_autoSendIndexes.Contains(x) && !_autoSendEntries.ContainsKey(x))
                    break;
                x++;
            }
            if (add)
                _autoSendIndexes.Add(x);
        }

        return x;
    }

    internal bool FreeAutoSendIndex(int index)
    {
        // Releasing a managed slot remains safe after the channel has been disposed.
        lock (_autoSendGate)
        {
            return _autoSendIndexes.Remove(index);
        }
    }

    internal void ApplyAutoSend(CcApi.VCI_AUTO_SEND_OBJ entry)
    {
        using var operation = EnterNativeOperation();
        lock (_autoSendGate)
        {
            // Retain even an uncertain write until a successful disable. A failed Stop
            // or PeriodicTx constructor must not hide a possibly active firmware entry.
            if (entry.Enable != 0) _autoSendEntries[entry.Index] = entry;
            WriteAutoSend(entry);
            if (entry.Enable == 0) _autoSendEntries.Remove(entry.Index);
        }
    }

    private unsafe void WriteAutoSend(CcApi.VCI_AUTO_SEND_OBJ entry)
        => ControlCanErr.ThrowIfErr(CcApi.VCI_SetReference(_rawDevType, _devIndex, CanIndex, 5, &entry),
            "VCI_SetReference(period)", this,
            $"failed to {(entry.Enable != 0 ? "enable" : "disable")} auto-send entry {entry.Index}.");

    private void DisableAutoSendEntries()
    {
        lock (_autoSendGate)
        {
            foreach (var saved in _autoSendEntries.Values.ToArray())
            {
                var entry = saved;
                entry.Enable = 0;
                TryCleanup($"disable auto-send entry {entry.Index}", () =>
                {
                    WriteAutoSend(entry);
                    _autoSendEntries.Remove(entry.Index);
                });
            }
        }
    }

    private bool ReadErrorInfo(out ICanErrorInfo? info)
    {
        info = null;
        if (CcApi.VCI_ReadErrInfo(_rawDevType, _devIndex, (uint)Options.ChannelIndex, out var err) == 0)
            return false;
        if (err.ErrCode == 0)
            return false;
        info = ControlCanErr.ToErrorInfo(err);
        return true;
    }

    internal ControlCanDeviceKind DevType => _devType;

    internal uint RawDevType => _rawDevType;

    internal uint DevIndex => _devIndex;
    internal uint CanIndex => (uint)Options.ChannelIndex;


}
