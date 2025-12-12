using System;
using System.Collections.Generic;
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
    private readonly object _evtGate = new();
    private readonly ITransceiver _transceiver;
    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<ICanErrorInfo>? _errorOccurred;
    private bool _isDisposed;
    private IDisposable? _owner;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private readonly AsyncFramePipe<CanReceiveData> _asyncRx;
    private Func<CanFrame, bool>? _softwareFilterPredicate;

    private readonly HashSet<int> _autoSendIndexes = new();
    private readonly object _autoSendGate = new();

    private readonly ControlCanDeviceKind _devType;
    private readonly uint _rawDevType;
    private readonly uint _devIndex;

    internal ControlCanBus(ControlCanDevice device, IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
    {
        Options = new ControlCanBusRtConfigurator();
        Options.Init((ControlCanBusOptions)options);
        _transceiver = transceiver;
        _asyncRx = new AsyncFramePipe<CanReceiveData>(Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null);

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

        // Build and apply initial configuration
        if (options.ProtocolMode != CanProtocolMode.Can20)
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, options.Features);

        var (t0, t1) = MapBaudRate(options.BitTiming);
        var cfg = new CcApi.VCI_INIT_CONFIG
        {
            AccCode = 0,
            AccMask = 0xFFFFFFFF,
            Filter = 0,
            Timing0 = t0,
            Timing1 = t1,
            Mode = options.WorkMode == ChannelWorkMode.ListenOnly ? (byte)1 : (byte)0,
        };

        var rules = options.Filter.FilterRules;
        if (rules.Count > 0 && rules[0] is FilterRule.Mask mask)
        {
            cfg.AccCode = mask.AccCode;
            cfg.AccMask = mask.AccMask;
            cfg.Filter = (byte)(mask.FilterIdType == CanFilterIDType.Extend ? 2 : 1);
        }

        CanKitLogger.LogInformation($"ControlCAN: Initializing dev={_devType}, idx={((ControlCanBusOptions)options).ChannelIndex} ch={options.ChannelIndex}, baud={options.BitTiming.Classic?.Nominal.Bitrate}");
        ApplyConfig(options);
        // Device already opened by ControlCanDevice; just init channel
        if (CcApi.VCI_InitCAN(_rawDevType, _devIndex, (uint)Options.ChannelIndex, ref cfg) == 0)
            throw new CanBusCreationException("VCI_InitCAN failed.");
        ApplyConfigAfterInit(options);
        Reset();
        if (CcApi.VCI_StartCAN(_rawDevType, _devIndex, (uint)Options.ChannelIndex) == 0)
            throw new CanBusCreationException("VCI_StartCAN failed.");

        NativeHandle = new BusNativeHandle((nint)((_rawDevType << 24) | (_devIndex << 16) | (uint)Options.ChannelIndex));
        StartReceiveLoop();
    }

    public BusNativeHandle NativeHandle { get; }

    public ControlCanBusRtConfigurator Options { get; }
    IBusRTOptionsConfigurator ICanBus.Options => Options;

    public void AttachOwner(IDisposable owner) => _owner = owner;

    public void ApplyConfig(IBusOptions options)
    {
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
    public void ApplyConfigAfterInit(IBusOptions options)
    {
        if ((options.Features & CanFeature.RangeFilter) != 0)
        {
            unsafe
            {
                var pRecord = stackalloc CcApi.VCI_FILTER_RECORD[1];
                var any = false;
                foreach (var filter in Options.Filter.FilterRules)
                {
                    if (filter is not FilterRule.Range range)
                    {
                        options.Filter.FilterRules.Add(filter);
                    }
                    else
                    {
                        pRecord->Start = range.From;
                        pRecord->End = range.To;
                        pRecord->ExtFrame = (range.FilterIdType == CanFilterIDType.Extend ? 1U : 0U);
                        ControlCanErr.ThrowIfErr(CcApi.VCI_SetReference(_rawDevType, _devIndex, CanIndex, 1, &pRecord), "VCI_SetReference(Filter)", this);
                        any = true;
                    }
                }

                if (any)
                {
                    ControlCanErr.ThrowIfErr(CcApi.VCI_SetReference(_rawDevType, _devIndex, CanIndex, 2, null), "VCI_SetReference(StartFilter)", this);
                }
            }
        }
        else
        {
            foreach (var filter in Options.Filter.FilterRules)
            {
                if (filter is not FilterRule.Mask)
                {
                    options.Filter.SoftwareFilterRules.Add(filter);
                }
            }
        }

        if (options.Filter.FilterRules.Count != 0)
        {
            _softwareFilterPredicate = FilterRule.Build(Options.Filter.SoftwareFilterRules);
        }
    }

    public void Reset()
    {
        ThrowIfDisposed();
        ControlCanErr.ThrowIfErr(CcApi.VCI_ResetCAN(_rawDevType, _devIndex, CanIndex), "VCI_ResetCAN()", this);
    }

    public void ClearBuffer()
    {
        ThrowIfDisposed();
        ControlCanErr.ThrowIfErr(CcApi.VCI_ResetCAN(_rawDevType, _devIndex, CanIndex), "VCI_ResetCAN()", this);
        _asyncRx.Clear();
    }

    public int Transmit(IEnumerable<CanFrame> frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames);
    }

    public int Transmit(ReadOnlySpan<CanFrame> frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames);
    }

    public int Transmit(CanFrame[] frames, int timeOut = 0) => Transmit(frames.AsSpan(), timeOut);
    public int Transmit(ArraySegment<CanFrame> frames, int timeOut = 0) => Transmit(frames.AsSpan(), timeOut);
    public int Transmit(in CanFrame frame) => _transceiver.Transmit(this, frame);

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
        ThrowIfDisposed();
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
        if (_isDisposed) return;
        try
        {
            _isDisposed = true;
            StopReceiveLoop();
        }
        finally
        {
            _owner?.Dispose();
        }
    }



    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new CanBusDisposedException();
    }

    private void StartReceiveLoop()
    {
        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoop(_pollCts.Token), _pollCts.Token);
        CanKitLogger.LogDebug("ControlCAN: Poll loop started.");
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
            Interlocked.CompareExchange(ref _pollTask, null, task);
            Interlocked.CompareExchange(ref _pollCts, null, cts);
            cts?.Dispose();
            CanKitLogger.LogDebug("ControlCAN: Poll loop stopped.");
        }
    }

    private void PollLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {

                DrainReceive();
                var errSnap = Volatile.Read(ref _errorOccurred);
                if (errSnap != null && Options.AllowErrorInfo && ReadErrorInfo(out var info) && info is not null)
                {
                    try
                    {
                        errSnap.Invoke(this, info);
                    }
                    catch (Exception e)
                    {
                        HandleBackgroundException(e, false);
                    }
                }
                PreciseDelay.Delay(TimeSpan.FromMilliseconds(Math.Max(1, Options.PollingInterval)), ct: token);
            }
        }
        catch (Exception ex)
        {
            HandleBackgroundException(ex, true);
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
                try
                {
                    var evSnap = Volatile.Read(ref _frameReceived);
                    evSnap?.Invoke(this, frame);
                }
                catch (Exception e)
                {
                    HandleBackgroundException(e, false);
                }
                _asyncRx.Publish(frame);
                any = true;
            }
            if (!any) break;
        }
    }

    private void HandleBackgroundException(Exception ex, bool fault)
    {
        try { CanKitLogger.LogError("ControlCAN bus occurred background exception.", ex); } catch { }

        if (fault)
        {
            try { _asyncRx.ExceptionOccured(ex); } catch { }

            try
            {
                var faultSpan = Volatile.Read(ref FaultOccurred);
                faultSpan?.Invoke(this, ex);
            }
            catch { }
            StopReceiveLoop();
        }

        try { var snap = Volatile.Read(ref BackgroundExceptionOccurred); snap?.Invoke(this, ex); } catch { }
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
                if (!_autoSendIndexes.Contains(x))
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
        ThrowIfDisposed();
        lock (_autoSendGate)
        {
            return _autoSendIndexes.Remove(index);
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
