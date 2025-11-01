using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.Kvaser.Utils;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Adapter.Kvaser.Native;
using CanKit.Core.Utils;

namespace CanKit.Adapter.Kvaser;

public sealed class KvaserBus : ICanBus<KvaserBusRtConfigurator>, IBusOwnership
{
    private readonly object _evtGate = new();

    private readonly int _handle;
    private readonly List<IDisposable> _owners = new();

    private readonly ITransceiver _transceiver;
    private int _pending;
    private int _drainRunning;
    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<ICanErrorInfo>? _errorOccured;

    private bool _isDisposed;

    private Canlib.kvCallbackDelegate? _kvCallback;
    private readonly AsyncFramePipe _asyncRx;
    private readonly Func<CanFrame, bool> _pred;

    internal KvaserBus(IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
    {
        Options = new KvaserBusRtConfigurator();
        Options.Init((KvaserBusOptions)options);
        _transceiver = transceiver;
        _asyncRx = new AsyncFramePipe(Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null);

        EnsureLibInitialized();

        options.Capabilities = ((KvaserProvider)provider).QueryCapabilities(options);
        options.Features = options.Capabilities.Features;

        // Open channel
        _handle = OpenChannel((KvaserBusOptions)options);
        CanKitLogger.LogInformation($"Kvaser: Initializing on '{_handle}', Mode={options.ProtocolMode}, Features={Options.Features}");
        if (_handle < 0)
        {
            throw new CanBusCreationException($"Kvaser canOpenChannel failed: handle={_handle}");
        }

        NativeHandle = new BusNativeHandle(_handle);

        // Configure bit timing and turn bus on
        ConfigureBitrate(_handle, (KvaserBusOptions)options);

        if (Options.ReceiveBufferCapacity != null)
        {
            int obj = Options.ReceiveBufferCapacity.Value;
            Canlib.canIoCtl(_handle, Canlib.canIOCTL_SET_RX_QUEUE_SIZE, ref obj, (uint)Marshal.SizeOf<int>());
        }

        var st = Canlib.canBusOn(_handle);
        CanKitLogger.LogInformation("PCAN: Initialize succeeded.");
        if (st != Canlib.canStatus.canOK)
        {
            Canlib.canClose(_handle);
            throw new CanBusCreationException($"Kvaser canBusOn failed: {st}");
        }

        // Apply initial options
        ApplyConfig(options);
        _pred = FilterRule.Build(Options.Filter.SoftwareFilterRules);
        CanKitLogger.LogDebug("Kvaser: Initial options applied.");

        _kvCallback ??= KvNotifyCallback;
        var mask = (Canlib.canNOTIFY_RX | (Options.AllowErrorInfo ? Canlib.canNOTIFY_ERROR : 0));
        KvaserUtils.ThrowIfError(Canlib.kvSetNotifyCallback(_handle, _kvCallback, IntPtr.Zero, (uint)mask),
            "kvSetNotifyCallback", "Failed to register notify callback");
    }

    public int Handle => _handle;


    public BusNativeHandle NativeHandle { get; }

    public void AttachOwner(IDisposable owner)
    {
        _owners.Add(owner);
    }

    public void ApplyConfig(ICanOptions options)
    {
        if (options is not KvaserBusOptions kc) return;
        if (kc.WorkMode == ChannelWorkMode.Echo)
        {
            var value = 1;
            KvaserUtils.ThrowIfError(
                Canlib.canIoCtl(_handle, Canlib.canIOCTL_SET_TXACK, ref value, (uint)Marshal.SizeOf<int>()),
                "canIoCtl(canIOCTL_SET_TXACK)",
                "Kvaser bus set echo mode failed");
        }
        // Set filter rules
        var rules = kc.Filter.FilterRules;
        if (rules.Count > 0)
        {
            foreach (var r in rules)
            {
                if (r is FilterRule.Mask mask)
                {
                    // Kvaser uses mask-based filters via canAccept
                    int ext = mask.FilterIdType == CanFilterIDType.Extend ? 1 : 0;
                    var st = Canlib.canSetAcceptanceFilter(_handle, mask.AccCode, mask.AccMask, ext);
                    if (st != Canlib.canStatus.canOK)
                    {
                        throw new CanBusConfigurationException($"Kvaser canSetAcceptanceFilter failed: {st}");
                    }
                }
                else
                {
                    // If software filter fallback enabled, push to software list; otherwise throw
                    if ((kc.EnabledSoftwareFallback & CanFeature.RangeFilter) != 0)
                    {
                        kc.Filter.SoftwareFilterRules.Add(r);
                    }
                    else
                    {
                        throw new CanFilterConfigurationException("Kvaser only supports mask filters via canAccept.");
                    }
                }
            }
        }

        // Apply timer_scale if supported by canlib
        try
        {
            // Prefer kvSetTimerScale(handle, scale)
            var mi = typeof(Canlib).GetMethod("kvSetTimerScale", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (mi != null)
            {
                var stObj = mi.Invoke(null, [_handle, kc.TimerScaleMicroseconds]);
                if (stObj is Canlib.canStatus st1 && st1 != Canlib.canStatus.canOK)
                {
                    CanKitLogger.LogWarning($"Kvaser: kvSetTimerScale failed: {st1}");
                }
            }
            else
            {
                // Fallback: canIoCtl with canIOCTL_SET_TIMER_SCALE
                var fi = typeof(Canlib).GetField("canIOCTL_SET_TIMER_SCALE", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (fi != null)
                {
                    var code = (uint)(fi.GetValue(null) ?? 0);
                    int val = kc.TimerScaleMicroseconds;
                    var st2 = Canlib.canIoCtl(_handle, code, ref val, (uint)Marshal.SizeOf<int>());
                    if (st2 != Canlib.canStatus.canOK)
                    {
                        CanKitLogger.LogWarning($"Kvaser: canIoCtl(SET_TIMER_SCALE) failed: {st2}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            CanKitLogger.LogDebug($"Kvaser: timer_scale not supported or failed to apply: {ex.Message}");
        }
    }

    //Ignored for canlib


    public void Reset()
    {
        ThrowIfDisposed();
        KvaserUtils.ThrowIfError(Canlib.canBusOff(_handle), "canBusOff", "Failed to bus-off during reset");
        KvaserUtils.ThrowIfError(Canlib.canBusOn(_handle), "canBusOn", "Failed to bus-on during reset");
        CanKitLogger.LogDebug("Kvaser: Channel reset.");
    }

    public void ClearBuffer()
    {
        ThrowIfDisposed();
        KvaserUtils.ThrowIfError(Canlib.canIoCtl(_handle, Canlib.canIOCTL_FLUSH_RX_BUFFER, IntPtr.Zero, 0U),
            "canIoCtl(FLUSH_RX_BUFFER)", "Failed to flush RX buffer");
        KvaserUtils.ThrowIfError(Canlib.canIoCtl(_handle, Canlib.canIOCTL_FLUSH_TX_BUFFER, IntPtr.Zero, 0U),
            "canIoCtl(FLUSH_TX_BUFFER)", "Failed to flush TX buffer");
    }

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

    public IPeriodicTx TransmitPeriodic(CanFrame frame, PeriodicTxOptions options)
    {
        ThrowIfDisposed();
        if (KvaserPeriodicTx.TryStart(this, frame, options, out var tx))
            return tx!;
        if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) != 0)
            return SoftwarePeriodicTx.Start(this, frame, options);

        throw new CanKitException(CanKitErrorCode.NativeCallFailed,
            "Failed to start hardware periodic transmit. The device may not support object buffers for cyclic TX; enable SoftwareFallback or check device capabilities.");
    }

    public float BusUsage()
    {
        CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.BusUsage);
        KvaserUtils.ThrowIfError(Canlib.canGetBusStatistics(_handle, out var stat,
                (UIntPtr)(uint)Marshal.SizeOf<Canlib.canBusStatistics>()), "canRequestBusStatistics",
            "Failed to get bus usage");
        return stat.busLoad / 100f;
    }

    public async Task<float> BusUsageAsync()
    {
        CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.BusUsage);
        KvaserUtils.ThrowIfError(Canlib.canRequestBusStatistics(_handle), "canRequestBusStatistics",
            "Failed to request bus usage");
        await Task.Delay(250);
        KvaserUtils.ThrowIfError(Canlib.canGetBusStatistics(_handle, out var stat,
                (UIntPtr)(uint)Marshal.SizeOf<Canlib.canBusStatistics>()), "canRequestBusStatistics",
            "Failed to get bus usage");
        return stat.busLoad / 100f;
    }

    public void RequestBusUsage()
    {
        CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.BusUsage);
        KvaserUtils.ThrowIfError(Canlib.canRequestBusStatistics(_handle), "canRequestBusStatistics",
            "Failed to request bus usage");
    }

    public CanErrorCounters ErrorCounters()
    {
        ThrowIfDisposed();
        CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.ErrorCounters);

        KvaserUtils.ThrowIfError(Canlib.canReadErrorCounters(_handle, out var tx, out var rx, out _),
            "canReadErrorCounters", "Failed to read error counters");
        return new CanErrorCounters()
        {
            TransmitErrorCounter = (int)tx,
            ReceiveErrorCounter = (int)rx
        };
    }

    public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return ReceiveAsync(count, timeOut).GetAwaiter().GetResult();
    }

    IBusRTOptionsConfigurator ICanBus.Options => Options;

    public KvaserBusRtConfigurator Options { get; }

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
            lock (_evtGate)
            {
                if (!Options.AllowErrorInfo)
                {
                    throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
                }
                _errorOccured += value;
            }
        }
        remove
        {
            lock (_evtGate)
            {
                if (!Options.AllowErrorInfo)
                {
                    throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
                }
                _errorOccured -= value;
            }
        }
    }

    public BusState BusState
    {
        get
        {
            ThrowIfDisposed();
            var st = Canlib.canReadStatus(_handle, out var status);
            if (st != Canlib.canStatus.canOK) return BusState.None;

            if ((status & Canlib.canSTAT_BUS_OFF) != 0) return BusState.BusOff;
            if ((status & Canlib.canSTAT_ERROR_PASSIVE) != 0) return BusState.ErrPassive;
            if ((status & Canlib.canSTAT_ERROR_WARNING) != 0) return BusState.ErrWarning;
            if ((status & Canlib.canSTAT_ERROR_ACTIVE) != 0) return BusState.ErrActive;
            return BusState.Unknown;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopReceiveLoop();
        try { _ = Canlib.canBusOff(_handle); } catch { }
        try { _ = Canlib.canClose(_handle); } catch { }
        foreach (var o in _owners) { try { o.Dispose(); } catch { } }
        _owners.Clear();
    }
    private void StopReceiveLoop()
    {
        try
        {
            _asyncRx.Clear();
            _ = Canlib.kvSetNotifyCallback(_handle, _kvCallback!, IntPtr.Zero, 0);
        }
        catch { /* ignore */ }
        finally
        {
            CanKitLogger.LogDebug("Kvaser: Notify callback unregistered.");
        }
    }
    private static void EnsureLibInitialized()
    {
        // canInitializeLibrary is safe to call multiple times
        try { Canlib.canInitializeLibrary(); }
        catch { /* ignore */ }
    }

    private static int OpenChannel(KvaserBusOptions opt)
    {
        int flags = 0;
        if (opt.AcceptVirtual) flags |= Canlib.canOPEN_ACCEPT_VIRTUAL;
        if (opt.ProtocolMode == CanProtocolMode.CanFd) flags |= Canlib.canOPEN_CAN_FD;
        return Canlib.canOpenChannel(opt.ChannelIndex, flags);
    }

    private static void ConfigureBitrate(int handle, KvaserBusOptions opt)
    {
        if (handle < 0) return;

        if (opt.ProtocolMode == CanProtocolMode.Can20)
        {
            var classic = opt.BitTiming.Classic!.Value;
            // Map common bitrates to predefined constants if available; otherwise, attempt a generic setup
            if (classic.Nominal.Segments is { } seg)
            {
                var st = Canlib.canSetBusParams(handle, (int)seg.BitRate(80),
                    (int)seg.Tseg1, (int)seg.Tseg2, (int)seg.Sjw, 1, 0);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanBusConfigurationException($"Kvaser canSetBusParams (classic segments) failed: {st}");
                }
            }
            else
            {
                var mapped = KvaserUtils.MapToKvaserConst((int)classic.Nominal.Bitrate!.Value);
                if (mapped == (int)classic.Nominal.Bitrate!.Value)
                {
                    throw new CanBusConfigurationException($"Unsupported classic bitrate: {classic.Nominal.Bitrate!.Value} bps for Kvaser predefined constants.");
                }
                var st = Canlib.canSetBusParams(handle, mapped, 0, 0, 0, 0, 0);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanBusConfigurationException($"Kvaser canSetBusParams (classic predefined) failed: {st}");
                }
            }
        }
        else if (opt.ProtocolMode == CanProtocolMode.CanFd)
        {
            var fd = opt.BitTiming.Fd!.Value;
            if (fd.Nominal.Segments is { } seg)
            {
                var st = Canlib.canSetBusParams(handle, (int)seg.BitRate(80),
                    (int)seg.Tseg1, (int)seg.Tseg2, (int)seg.Sjw, 1, 0);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanBusConfigurationException($"Kvaser canSetBusParams (FD nominal segments) failed: {st}");
                }
            }
            else
            {
                var mapped = KvaserUtils.MapToKvaserConst((int)fd.Nominal.Bitrate!.Value);
                if (mapped == (int)fd.Nominal.Bitrate!.Value)
                {
                    throw new CanBusConfigurationException($"Unsupported FD nominal bitrate: {fd.Nominal.Bitrate!.Value} bps for Kvaser predefined constants.");
                }
                var st = Canlib.canSetBusParams(handle, mapped, 0, 0, 0, 0, 0);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanBusConfigurationException($"Kvaser canSetBusParams (FD nominal predefined) failed: {st}");
                }
            }
            if (fd.Data.Segments is { } seg1)
            {
                var st = Canlib.canSetBusParamsFd(handle, (int)seg1.BitRate(80),
                    (int)seg1.Tseg1, (int)seg1.Tseg2, (int)seg1.Sjw);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanBusConfigurationException($"Kvaser canSetBusParamsFd (FD data segments) failed: {st}");
                }
            }
            else
            {
                var mappedFd = KvaserUtils.MapToKvaserFdConst((int)fd.Data.Bitrate!.Value,
                    fd.Data.SamplePointPermille ?? 80);
                if (mappedFd == (int)fd.Data.Bitrate!.Value)
                {
                    throw new CanBusConfigurationException($"Unsupported FD data bitrate/SP combination: {fd.Data.Bitrate!.Value} bps @ {fd.Data.SamplePointPermille ?? 80}%.");
                }
                var st = Canlib.canSetBusParamsFd(handle, mappedFd, 0, 0, 0);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanBusConfigurationException($"Kvaser canSetBusParamsFd (FD data predefined) failed: {st}");
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new CanBusDisposedException();
    }

    private void KvNotifyCallback(int handle, IntPtr context, int notifyEvent)
    {
        if (handle != _handle) return;
        Interlocked.Increment(ref _pending);

        if (Interlocked.Exchange(ref _drainRunning, 1) == 0)
        {
            Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        Interlocked.Exchange(ref _pending, 0);
                        DrainReceive();

                        if (Interlocked.Exchange(ref _pending, 0) == 0)
                            break;
                    }
                }
                catch (Exception ex)
                {
                    HandleBackgroundException(ex); //will not throw in poll loop
                }
                finally
                {
                    Volatile.Write(ref _drainRunning, 0);
                    if ((Volatile.Read(ref _pending) > 0 &&
                         Volatile.Read(ref _drainRunning) == 0))
                    {
                        Task.Run(() => KvNotifyCallback(_handle, IntPtr.Zero, 0));
                    }
                }
            });
        }
    }

    private void DrainReceive()
    {
        while (true)
        {
            var any = false;
            foreach (var rec in _transceiver.Receive(this, 64))
            {
                any = true;

                if (rec.CanFrame.IsErrorFrame)
                {
                    var enableErrorCounter = (Options.Features & CanFeature.ErrorCounters) != 0;
                    CanErrorCounters? errorCounters = enableErrorCounter ? ErrorCounters() : null;
                    _errorOccured?.Invoke(this, new DefaultCanErrorInfo(
                        FrameErrorType.Unknown,
                        enableErrorCounter ?
                            CanKitExtension.ToControllerStatus(errorCounters!.Value.ReceiveErrorCounter, errorCounters.Value.TransmitErrorCounter) : CanControllerStatus.Unknown,
                        CanProtocolViolationType.Unknown,
                        FrameErrorLocation.Invalid,
                        CanTransceiverStatus.Unknown,
                        DateTime.Now,
                        0,
                        null,
                        FrameDirection.Unknown,
                        null,
                        errorCounters,
                        null
                    ));
                    continue;
                }

                var useSw = (Options.EnabledSoftwareFallback & CanFeature.RangeFilter) != 0 && _pred is not null;
                if (useSw)
                {
                    if (!_pred!(rec.CanFrame))
                    {
                        continue;
                    }
                }
                _frameReceived?.Invoke(this, rec);
                _asyncRx.Publish(rec);
            }
            if (!any) break;
        }
    }

    public Task<int> TransmitAsync(IEnumerable<CanFrame> frames, int _ = 0, CancellationToken cancellationToken = default)
        => Task.FromResult(Transmit(frames));

    public Task<int> TransmitAsync(CanFrame frame, CancellationToken cancellationToken = default)
        => Task.FromResult(Transmit(frame));
    public async Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return await _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken)
            .ConfigureAwait(false);
    }

    public event EventHandler<Exception>? BackgroundExceptionOccurred;

    private void HandleBackgroundException(Exception ex)
    {
        try { CanKitLogger.LogError("Kvaser bus occured background exception.", ex); } catch { /*ignored*/ }
        try { _asyncRx.ExceptionOccured(ex); } catch { /*ignored*/ }
        try { var snap = Volatile.Read(ref BackgroundExceptionOccurred); snap?.Invoke(this, ex); } catch { /*ignored*/ }
    }

    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await foreach (var item in _asyncRx.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
}
