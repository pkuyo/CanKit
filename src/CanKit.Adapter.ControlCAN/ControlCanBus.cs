using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Adapter.ControlCAN.Definitions;
using CanKit.Adapter.ControlCAN.Native;
using CanKit.Adapter.ControlCAN.Options;
using CcApi = CanKit.Adapter.ControlCAN.Native.ControlCAN;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;
using CanKit.Adapter.ControlCAN.Diagnostics;

namespace CanKit.Adapter.ControlCAN;

public sealed class ControlCanBus : ICanBus<ControlCanBusRtConfigurator>, IBusOwnership
{
    private readonly object _evtGate = new();
    private readonly ITransceiver _transceiver;
    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<ICanErrorInfo>? _errorOccurred;
    private bool _isDisposed;
    private IDisposable? _owner;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private readonly AsyncFramePipe _asyncRx;
    private int _asyncConsumerCount;
    private int _subscriberCount;
    private CancellationTokenSource? _stopDelayCts;
    private int _asyncBufferingLinger;
    private Func<ICanFrame, bool>? _softwareFilterPredicate;

    private readonly uint _devType;
    private readonly uint _devIndex;
    private readonly bool _isUsbcane;

    internal ControlCanBus(ControlCanDevice device, IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
    {
        Options = new ControlCanBusRtConfigurator();
        Options.Init((ControlCanBusOptions)options);
        _transceiver = transceiver;
        _asyncRx = new AsyncFramePipe(Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null);

        var dt = (ControlCanDeviceType)device.Options.DeviceType;
        _devType = (uint)dt.Code;
        _devIndex = device.Options.DeviceIndex;
        _isUsbcane = IsUsbcaneSeries(dt);

        // Reflect capabilities from provider
        if (provider is ICanCapabilityProvider sniffer)
            options.Capabilities = sniffer.QueryCapabilities(options);
        else
            options.Capabilities = new Capability(provider.StaticFeatures);
        options.Features = options.Capabilities.Features;

        // Build and apply initial configuration
        if (options.ProtocolMode != CanProtocolMode.Can20)
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, options.Features);

        var (t0, t1) = MapBaudRate(options.BitTiming);
        var cfg = new CcApi.VCI_INIT_CONFIG
        {
            AccCode = 0,
            AccMask = 0xFFFFFFFF,
            Filter = 1,
            Timing0 = t0,
            Timing1 = t1,
            Mode = options.WorkMode == ChannelWorkMode.ListenOnly ? (byte)1 : (byte)0,
        };

        var rules = options.Filter.filterRules;
        if (rules.Count > 0 && rules[0] is FilterRule.Mask mask)
        {
            cfg.AccCode = mask.AccCode;
            cfg.AccMask = mask.AccMask;
            cfg.Filter = (byte)(mask.FilterIdType == CanFilterIDType.Extend ? 2 : 1);
        }

        // Prepare software filter fallbacks per device capability
        var isUsbcane = IsUsbcaneSeries((ControlCanDeviceType)device.Options.DeviceType);
        if (rules.Count > 0)
        {
        }

        CanKitLogger.LogInformation($"ControlCAN: Initializing dev={_devType}, idx={((ControlCanBusOptions)options).ChannelIndex} ch={options.ChannelIndex}, baud={options.BitTiming.Classic?.Nominal.Bitrate}");

        // Device already opened by ControlCanDevice; just init channel
        if (CcApi.VCI_InitCAN(_devType, _devIndex, (uint)Options.ChannelIndex, ref cfg) == 0)
            throw new CanBusCreationException("VCI_InitCAN failed.");

        Reset();
        if (CcApi.VCI_StartCAN(_devType, _devIndex, (uint)Options.ChannelIndex) == 0)
            throw new CanBusCreationException("VCI_StartCAN failed.");

        NativeHandle = new BusNativeHandle((nint)((_devType << 24) | (_devIndex << 16) | (uint)Options.ChannelIndex));
    }

    public BusNativeHandle NativeHandle { get; }

    public ControlCanBusRtConfigurator Options { get; }
    IBusRTOptionsConfigurator ICanBus.Options => Options;

    public void AttachOwner(IDisposable owner) => _owner = owner;

    public void ApplyConfig(ICanOptions options)
    {
        //TODO:
    }

    public void Reset()
    {
        ThrowIfDisposed();
        _ = CcApi.VCI_ResetCAN(_devType, _devIndex, (uint)Options.ChannelIndex);
    }

    public void ClearBuffer()
    {
        ThrowIfDisposed();
        _ = CcApi.VCI_ClearBuffer(_devType, _devIndex, (uint)Options.ChannelIndex);
        _asyncRx.Clear();
    }

    public int Transmit(IEnumerable<ICanFrame> frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames);
    }

    public int Transmit(ReadOnlySpan<ICanFrame> frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames);
    }

    public int Transmit(ICanFrame[] frames, int timeOut = 0) => Transmit(frames.AsSpan(), timeOut);
    public int Transmit(ArraySegment<ICanFrame> frames, int timeOut = 0) => Transmit(frames.AsSpan(), timeOut);
    public int Transmit(in ICanFrame frame) => _transceiver.Transmit(this, frame);

    public IPeriodicTx TransmitPeriodic(ICanFrame frame, PeriodicTxOptions options)
    {
        // Prefer hardware periodic on USBCAN-E series; fallback to software if requested.
        if (_isUsbcane)
        {
            //return ControlCanPeriodicTx.Start(this, frame, options);
            //TODO
        }
        if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) != 0)
            return SoftwarePeriodicTx.Start(this, frame, options);
        throw new CanFeatureNotSupportedException(CanFeature.CyclicTx, Options.Features);
    }

    public Task<int> TransmitAsync(IEnumerable<ICanFrame> frames, int timeOut = 0, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            try { return Transmit(frames, timeOut); }
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

    public float BusUsage() => throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Features);
    public CanErrorCounters ErrorCounters()
    {
        ThrowIfDisposed();
        if (CcApi.VCI_ReadErrInfo(_devType, _devIndex, (uint)Options.ChannelIndex, out var err) == 0)
            return default;

        var rec = err.Passive_ErrData != null && err.Passive_ErrData.Length >= 2 ? err.Passive_ErrData[1] : (byte)0;
        var tec = err.Passive_ErrData != null && err.Passive_ErrData.Length >= 3 ? err.Passive_ErrData[2] : (byte)0;
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
        Interlocked.Increment(ref _subscriberCount);
        Interlocked.Increment(ref _asyncConsumerCount);
        Volatile.Write(ref _asyncBufferingLinger, 0);
        StartReceiveLoopIfNeeded();
        try
        {
            return await _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken).ConfigureAwait(false);
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
                yield return item;
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

    public BusState BusState => BusState.Unknown;

    public event EventHandler<CanReceiveData>? FrameReceived
    {
        add
        {
            bool needStart = false;
            lock (_evtGate)
            {
                var before = _frameReceived;
                _frameReceived += value;
                if (!ReferenceEquals(before, _frameReceived))
                {
                    Interlocked.Increment(ref _subscriberCount);
                    needStart = (before == null) && Volatile.Read(ref _asyncConsumerCount) == 0;
                }
            }
            if (needStart) StartReceiveLoopIfNeeded();
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
                throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
            bool needStart = false;
            lock (_evtGate)
            {
                var before = _errorOccurred;
                _errorOccurred += value;
                if (!ReferenceEquals(before, _errorOccurred))
                {
                    Interlocked.Increment(ref _subscriberCount);
                    needStart = (before == null) && Volatile.Read(ref _asyncConsumerCount) == 0;
                }
            }
            if (needStart) StartReceiveLoopIfNeeded();
        }
        remove
        {
            if (!Options.AllowErrorInfo)
                throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
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
            if (needStop) RequestStopReceiveLoop();
        }
    }

    public event EventHandler<Exception>? BackgroundExceptionOccurred;

    public void Dispose()
    {
        if (_isDisposed) return;
        try
        {
            StopReceiveLoop();
        }
        finally
        {
            _isDisposed = true;
            _owner?.Dispose();
        }
    }



    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new CanBusDisposedException();
    }

    private void StartReceiveLoopIfNeeded()
    {
        if (_pollTask != null) return;
        _pollCts = new CancellationTokenSource();
        DrainReceive();
        _pollTask = Task.Run(() => PollLoop(_pollCts.Token), _pollCts.Token);
        CanKitLogger.LogDebug("ControlCAN: Poll loop started.");
    }

    private void StopReceiveLoop()
    {
        try
        {
            _asyncRx.Clear();
            _pollCts?.Cancel();
            _pollTask?.Wait(200);
        }
        catch { }
        finally
        {
            _pollTask = null;
            _pollCts?.Dispose();
            _pollCts = null;
            Volatile.Write(ref _asyncBufferingLinger, 0);
            CanKitLogger.LogDebug("ControlCAN: Poll loop stopped.");
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
                    StopReceiveLoop();
            }
            catch { }
        }, cts.Token);
    }

    private void PollLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Volatile.Read(ref _subscriberCount) == 0 && Volatile.Read(ref _asyncBufferingLinger) == 0)
                    break;

                DrainReceive();
                var errSnap = Volatile.Read(ref _errorOccurred);
                if (errSnap != null && Options.AllowErrorInfo && ReadErrorInfo(out var info) && info is not null)
                {
                    try { errSnap.Invoke(this, info); } catch { }
                }
                Thread.Sleep(Math.Max(1, Options.PollingInterval));
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
            foreach (var frame in _transceiver.Receive(this, CcApi.BATCH_COUNT))
            {
                if (_softwareFilterPredicate != null && !_softwareFilterPredicate(frame.CanFrame))
                    continue;
                _frameReceived?.Invoke(this, frame);
                if (Volatile.Read(ref _asyncConsumerCount) > 0 || Volatile.Read(ref _asyncBufferingLinger) == 1)
                    _asyncRx.Publish(frame);
                any = true;
            }
            if(!any) break;
        }
    }

    private void HandleBackgroundException(Exception ex)
    {
        try { CanKitLogger.LogError("ControlCAN bus occurred background exception.", ex); } catch { }
        try { _asyncRx.ExceptionOccured(ex); } catch { }
        try { var snap = Volatile.Read(ref BackgroundExceptionOccurred); snap?.Invoke(this, ex); } catch { }
    }

    private static (byte t0, byte t1) MapBaudRate(CanBusTiming timing)
    {
        if (!timing.Classic.HasValue || !timing.Classic.Value.Nominal.IsTarget)
            throw new CanBusConfigurationException("Classic timing must specify a target nominal bitrate.");
        var bps = timing.Classic.Value.Nominal.Bitrate!.Value;
        // Typical SJA1000 @ 8MHz timing table

        //TODO:
        return bps switch
        {
            _ => throw new CanBusConfigurationException($"Unsupported ControlCAN classic bitrate: {bps}")
        };
    }

    internal uint DevType => _devType;
    internal uint DevIndex => _devIndex;
    internal uint CanIndex => (uint)Options.ChannelIndex;

    private static bool IsUsbcaneSeries(ControlCanDeviceType t)
        => t.Code is 20 or 21 or 31 or 34;

    private bool ReadErrorInfo(out ICanErrorInfo? info)
    {
        info = null;
        if (CcApi.VCI_ReadErrInfo(_devType, _devIndex, (uint)Options.ChannelIndex, out var err) == 0)
            return false;
        if (err.ErrCode == 0)
            return false;
        info = ControlCanErr.ToErrorInfo(err);
        return true;
    }
}
