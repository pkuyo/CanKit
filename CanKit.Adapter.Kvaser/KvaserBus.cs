using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Adapter.Kvaser.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using Kvaser.CanLib;
using CanKit.Core.Utils;

namespace CanKit.Adapter.Kvaser;

public sealed class KvaserBus : ICanBus<KvaserBusRtConfigurator>, ICanApplier, IBusOwnership
{
    private readonly object _evtGate = new();

    private readonly int _handle;
    private readonly List<IDisposable> _owners = new();

    private readonly ITransceiver _transceiver;
    private int _drainRunning;
    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<ICanErrorInfo>? _errorOccured;
    private bool _isDisposed;
    private Canlib.kvCallbackDelegate? _kvCallback;
    private bool _notifyActive;
    private int _subscriberCount;

    internal KvaserBus(IBusOptions options, ITransceiver transceiver)
    {
        Options = new KvaserBusRtConfigurator();
        Options.Init((KvaserBusOptions)options);
        _transceiver = transceiver;

        EnsureLibInitialized();

        // Open channel
        _handle = OpenChannel((KvaserBusOptions)options);
        if (_handle < 0)
        {
            throw new CanBusCreationException($"Kvaser canOpenChannel failed: handle={_handle}");
        }

        // Configure bit timing and turn bus on
        ConfigureBitrate(_handle, (KvaserBusOptions)options);

        var st = Canlib.canBusOn(_handle);
        if (st != Canlib.canStatus.canOK)
        {
            Canlib.canClose(_handle);
            throw new CanBusCreationException($"Kvaser canBusOn failed: {st}");
        }

        // Apply initial options (filters, error options)
        options.Apply(this, true);
        CanKitLogger.LogDebug("Kvaser: Initial options applied.");
    }

    public int Handle => _handle;

    public void AttachOwner(IDisposable owner)
    {
        _owners.Add(owner);
    }

    public void Apply(ICanOptions options)
    {
        if (options is not KvaserBusOptions kc) return;

        // Set filter rules
        var rules = kc.Filter.filterRules;
        if (rules.Count > 0)
        {
            foreach (var r in rules)
            {
                if (r is FilterRule.Mask mask)
                {
                    // Kvaser uses mask-based filters via canAccept
                    int ext = mask.FilterIdType == CanFilterIDType.Extend ? 1 : 0;
                    var st = Canlib.canSetAcceptanceFilter(_handle, (int)mask.AccCode, (int)mask.AccMask, ext);
                    if (st != Canlib.canStatus.canOK)
                    {
                        throw new CanChannelConfigurationException($"Kvaser canSetAcceptanceFilter failed: {st}");
                    }
                }
                else
                {
                    // If software filter fallback enabled, push to software list; otherwise throw
                    if ((kc.EnabledSoftwareFallback & CanFeature.Filters) != 0)
                    {
                        kc.Filter.softwareFilter.Add(r);
                    }
                    else
                    {
                        throw new CanFilterConfigurationException("Kvaser CANlib only supports mask filters via canAccept.");
                    }
                }
            }
        }

        // Apply timer_scale if supported by CANlib
        try
        {
            // Prefer kvSetTimerScale(handle, scale)
            var mi = typeof(Canlib).GetMethod("kvSetTimerScale", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (mi != null)
            {
                var stObj = mi.Invoke(null, new object[] { _handle, kc.TimerScaleMicroseconds });
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
                    var code = (int)(fi.GetValue(null) ?? 0);
                    object val = kc.TimerScaleMicroseconds;
                    var st2 = Canlib.canIoCtl(_handle, code, ref val);
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

    public CanOptionType ApplierStatus => CanOptionType.Runtime;


    public void Reset()
    {
        ThrowIfDisposed();
        KvaserUtils.ThrowIfError(Canlib.canBusOff(_handle), "canBusOff", "Failed to bus-off during reset");
        KvaserUtils.ThrowIfError(Canlib.canBusOn(_handle), "canBusOn", "Failed to bus-on during reset");
        CanKitLogger.LogDebug("Kvaser: Channel reset issued.");
    }

    public void ClearBuffer()
    {
        ThrowIfDisposed();
        object? obj = null;
        KvaserUtils.ThrowIfError(Canlib.canIoCtl(_handle, Canlib.canIOCTL_FLUSH_RX_BUFFER, ref obj),
            "canIoCtl(FLUSH_RX_BUFFER)", "Failed to flush RX buffer");
        KvaserUtils.ThrowIfError(Canlib.canIoCtl(_handle, Canlib.canIOCTL_FLUSH_TX_BUFFER, ref obj),
            "canIoCtl(FLUSH_TX_BUFFER)", "Failed to flush TX buffer");
    }

    public uint Transmit(IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames, timeOut);
    }

    public IPeriodicTx TransmitPeriodic(CanTransmitData frame, PeriodicTxOptions options)
    {
        ThrowIfDisposed();
        if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) != 0)
            return SoftwarePeriodicTx.Start(this, frame, options);
        else
            throw new CanFeatureNotSupportedException(CanFeature.CyclicTx, Options.Features);
    }

    public float BusUsage() => throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Features);

    public CanErrorCounters ErrorCounters()
    {
        ThrowIfDisposed();
        KvaserUtils.ThrowIfError(Canlib.canReadErrorCounters(_handle, out var tx, out var rx, out _),
            "canReadErrorCounters", "Failed to read error counters");
        return new CanErrorCounters()
        {
            TransmitErrorCounter = tx,
            ReceiveErrorCounter = rx
        };
    }

    public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Receive(this, count, timeOut);
    }

    public bool ReadErrorInfo(out ICanErrorInfo? errorInfo)
    {
        // Kvaser can report error frames in stream; surface as ErrorOccurred not implemented
        errorInfo = null;
        return false;
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
                _subscriberCount++;
                StartNotifyIfNeeded();
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _frameReceived -= value;
                _subscriberCount = Math.Max(0, _subscriberCount - 1);
                if (_subscriberCount == 0) StopNotify();
            }
        }
    }

    public event EventHandler<ICanErrorInfo>? ErrorOccurred
    {
        add
        {
            lock (_evtGate)
            {
                _errorOccured += value;
                _subscriberCount++;
                StartNotifyIfNeeded();
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _errorOccured -= value;
                _subscriberCount = Math.Max(0, _subscriberCount - 1);
                if (_subscriberCount == 0) StopNotify();
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
            return BusState.None;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopNotify();
        try { _ = Canlib.canBusOff(_handle); } catch { }
        try { _ = Canlib.canClose(_handle); } catch { }
        foreach (var o in _owners) { try { o.Dispose(); } catch { } }
        _owners.Clear();
        GC.SuppressFinalize(this);
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
        if (opt.AcceptVirtual) flags |= (int)Canlib.canOPEN_ACCEPT_VIRTUAL;
        // Try name lookup if provided
        if (!string.IsNullOrWhiteSpace(opt.ChannelName))
        {
            // Enumerate channels to match by name
            int n = 0;
            if (Canlib.canGetNumberOfChannels(out n) == Canlib.canStatus.canOK)
            {
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        Canlib.canGetChannelData(i, Canlib.canCHANNELDATA_CHANNEL_NAME, out var obj);
                        if (obj is string name && !string.IsNullOrWhiteSpace(name) &&
                            string.Equals(name, opt.ChannelName, StringComparison.OrdinalIgnoreCase))
                        {
                            return Canlib.canOpenChannel(i, flags);
                        }
                    }
                    catch { }

                }
            }
        }

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
                    (int)seg.Tseg1, (int)seg.Tseg2, (int)seg.Sjw, 1);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanChannelConfigurationException($"Kvaser canSetBusParams (classic segments) failed: {st}");
                }
            }
            else
            {
                var mapped = KvaserUtils.MapToKvaserConst((int)classic.Nominal.Bitrate!.Value);
                if (mapped == (int)classic.Nominal.Bitrate!.Value)
                {
                    throw new CanChannelConfigurationException($"Unsupported classic bitrate: {classic.Nominal.Bitrate!.Value} bps for Kvaser predefined constants.");
                }
                var st = Canlib.canSetBusParams(handle, mapped, 0, 0, 0, 0);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanChannelConfigurationException($"Kvaser canSetBusParams (classic predefined) failed: {st}");
                }
            }
        }
        else if (opt.ProtocolMode == CanProtocolMode.CanFd)
        {
            var fd = opt.BitTiming.Fd!.Value;
            if (fd.Nominal.Segments is { } seg)
            {
                var st = Canlib.canSetBusParams(handle, (int)seg.BitRate(80),
                    (int)seg.Tseg1, (int)seg.Tseg2, (int)seg.Sjw, 1);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanChannelConfigurationException($"Kvaser canSetBusParams (FD nominal segments) failed: {st}");
                }
            }
            else
            {
                var mapped = KvaserUtils.MapToKvaserConst((int)fd.Nominal.Bitrate!.Value);
                if (mapped == (int)fd.Nominal.Bitrate!.Value)
                {
                    throw new CanChannelConfigurationException($"Unsupported FD nominal bitrate: {fd.Nominal.Bitrate!.Value} bps for Kvaser predefined constants.");
                }
                var st = Canlib.canSetBusParams(handle, mapped, 0, 0, 0, 0);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanChannelConfigurationException($"Kvaser canSetBusParams (FD nominal predefined) failed: {st}");
                }
            }
            if (fd.Data.Segments is { } seg1)
            {
                var st = Canlib.canSetBusParamsFd(handle, (int)seg1.BitRate(80),
                    (int)seg1.Tseg1, (int)seg1.Tseg2, (int)seg1.Sjw);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanChannelConfigurationException($"Kvaser canSetBusParamsFd (FD data segments) failed: {st}");
                }
            }
            else
            {
                var mappedFd = KvaserUtils.MapToKvaserFdConst((int)fd.Data.Bitrate!.Value,
                    fd.Data.SamplePointPermille ?? 80);
                if (mappedFd == (int)fd.Data.Bitrate!.Value)
                {
                    throw new CanChannelConfigurationException($"Unsupported FD data bitrate/SP combination: {fd.Data.Bitrate!.Value} bps @ {fd.Data.SamplePointPermille ?? 80}%.");
                }
                var st = Canlib.canSetBusParamsFd(handle, mappedFd, 0, 0, 0);
                if (st != Canlib.canStatus.canOK)
                {
                    throw new CanChannelConfigurationException($"Kvaser canSetBusParamsFd (FD data predefined) failed: {st}");
                }
            }
        }
        else
        {
            //TODO:
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new ObjectDisposedException(GetType().FullName);
    }

    private void StartNotifyIfNeeded()
    {
        if (_notifyActive) return;
        _kvCallback ??= KvNotifyCallback;
        var mask = (Canlib.canNOTIFY_RX | Canlib.canNOTIFY_ERROR | Canlib.canNOTIFY_STATUS);
        KvaserUtils.ThrowIfError(Canlib.kvSetNotifyCallback(_handle, _kvCallback, IntPtr.Zero, mask),
            "kvSetNotifyCallback", "Failed to register notify callback");
        _notifyActive = true;
    }

    private void StopNotify()
    {
        if (!_notifyActive) return;
        try
        {
            // Unregister by clearing mask or null callback depending on API behavior
            _ = Canlib.kvSetNotifyCallback(_handle, _kvCallback, IntPtr.Zero, 0);
        }
        catch { /* ignore */ }
        finally
        {
            _notifyActive = false;
            CanKitLogger.LogDebug("Kvaser: Notify callback unregistered.");
        }
    }

    private void KvNotifyCallback(int handle, IntPtr context, int notifyEvent)
    {
        // Sanity checks
        if (handle != _handle) return;
        if (Volatile.Read(ref _subscriberCount) <= 0) return;

        // Debounce and drain on thread-pool to avoid heavy work in callback
        if (Interlocked.Exchange(ref _drainRunning, 1) == 0)
        {
            Task.Run(() =>
            {
                try { DrainReceive(); }
                finally { Volatile.Write(ref _drainRunning, 0); }
            });
        }
    }

    private void DrainReceive()
    {
        while (true)
        {
            var any = false;
            foreach (var rec in _transceiver.Receive(this, 64, 0))
            {
                any = true;

                if (rec.CanFrame.IsErrorFrame)
                {
                    var errorCounters = ErrorCounters();
                    _errorOccured?.Invoke(this, new DefaultCanErrorInfo(
                        FrameErrorType.Unknown,
                        CanKitExtension.ToControllerStatus(errorCounters.ReceiveErrorCounter, errorCounters.TransmitErrorCounter),
                        CanProtocolViolationType.Unknown,
                        FrameErrorLocation.Invalid,
                        DateTime.Now,
                        0,
                        null,
                        FrameDirection.Unknown,
                        null,
                        CanTransceiverStatus.Unknown,
                        errorCounters,
                        null
                        ));
                    continue;
                }

                var useSw = (Options.EnabledSoftwareFallback & CanFeature.Filters) != 0 && Options.Filter.SoftwareFilterRules.Count > 0;
                if (useSw)
                {
                    var pred = FilterRule.Build(Options.Filter.SoftwareFilterRules);
                    if (!pred(rec.CanFrame))
                    {
                        continue;
                    }
                }
                _frameReceived?.Invoke(this, rec);
            }
            if (!any) break;
        }
    }
}
