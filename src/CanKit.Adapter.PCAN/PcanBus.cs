
#if NETSTANDARD2_0
using System.Runtime.InteropServices;
#endif
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;
using Microsoft.Win32.SafeHandles;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN;

public sealed class PcanBus : ICanBus<PcanBusRtConfigurator>, ICanApplier, IBusOwnership
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

    // Cached software filter predicate to avoid rebuilding on each poll
    private Func<ICanFrame, bool>? _softwareFilterPredicate;
    //private EventHandler<ICanErrorInfo>? _errorOccurred;

    private int _subscriberCount;
    private bool _useSoftwareFilter;
    private readonly AsyncFramePipe _asyncRx;
    private int _asyncConsumerCount;
    private CancellationTokenSource? _stopDelayCts;


    internal PcanBus(IBusOptions options, ITransceiver transceiver)
    {
        Options = new PcanBusRtConfigurator();
        Options.Init((PcanBusOptions)options);
        _transceiver = transceiver;
        _asyncRx = new AsyncFramePipe(Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null);

        _handle = ParseHandle();

        // Discover runtime capabilities (e.g., FD) and merge to dynamic features
        SniffDynamicFeatures();
        CanKitLogger.LogInformation($"PCAN: Initializing on '{options.ChannelName}', Mode={options.ProtocolMode}, Features={Options.Features}");

        // If requested FD but not supported at runtime, fail early
        if (options.ProtocolMode == CanProtocolMode.CanFd && (Options.Features & CanFeature.CanFd) == 0)
        {
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, Options.Features);
        }

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
        else
        {
            //TODO:
        }

        // Apply initial options (filters etc.)
        options.Apply(this, true);
        CanKitLogger.LogDebug("PCAN: Initial options applied.");
#if NETSTANDARD2_0
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
#else
#if WINDOWS
        _recEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
#else
        var ok = Api.GetValue(_handle, PcanParameter.ReceiveEvent, out uint evHandle);
        if (ok != PcanStatus.OK)
            throw new InvalidOperationException($"Get ReceiveEvent failed: {ok}");

        _recEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        _recEvent.SafeWaitHandle.Close();
        _recEvent.SafeWaitHandle = new SafeWaitHandle(new IntPtr(evHandle), false);
#endif
#endif
    }


    public PcanStatus PCanState => Api.GetStatus(_handle);

    internal PcanChannel Handle => _handle;

    public void AttachOwner(IDisposable owner)
    {
        _owner = owner;
    }

    public void Apply(ICanOptions options)
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
                    _ = Api.FilterMessages(_handle, rg.From, rg.To, mode);
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
            _useSoftwareFilter = (Options.EnabledSoftwareFallback & CanFeature.Filters) != 0
                                 && Options.Filter.SoftwareFilterRules.Count > 0;
            _softwareFilterPredicate = _useSoftwareFilter
                ? FilterRule.Build(Options.Filter.SoftwareFilterRules)
                : null;
        }

        if (pc.AllowErrorInfo)
        {
            Api.SetValue(_handle, PcanParameter.AllowErrorFrames, ParameterValue.Activation.On);
        }



    }

    public CanOptionType ApplierStatus => CanOptionType.Runtime;


    public void Reset()
    {
        ThrowIfDisposed();
        _ = Api.Reset(_handle);
        CanKitLogger.LogDebug("PCAN: Channel reset issued.");
    }

    public void ClearBuffer()
    {
        ThrowIfDisposed();
        // Reset clears the receive/transmit queues
        _ = Api.Reset(_handle);
        CanKitLogger.LogDebug("PCAN: Buffers cleared via reset.");

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

    public CanErrorCounters ErrorCounters() => throw new CanFeatureNotSupportedException(CanFeature.ErrorCounters, Options.Features);

    public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Receive(this, count, timeOut);
    }

    public Task<uint> TransmitAsync(IEnumerable<CanTransmitData> frames, int timeOut = 0, CancellationToken cancellationToken = default)
        => Task.Run(() => Transmit(frames, timeOut), cancellationToken);

    public Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(uint count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _subscriberCount);
        Interlocked.Increment(ref _asyncConsumerCount);
        StartReceiveLoopIfNeeded();
        return _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken)
            .ContinueWith(t =>
            {
                var remAsync = Interlocked.Decrement(ref _asyncConsumerCount);
                var rem = Interlocked.Decrement(ref _subscriberCount);
                if (rem == 0 && remAsync == 0) RequestStopReceiveLoop();
                return t.Result;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

#if NET8_0_OR_GREATER
    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _subscriberCount);
        Interlocked.Increment(ref _asyncConsumerCount);
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
            if (rem == 0 && remAsync == 0) RequestStopReceiveLoop();
        }
    }
#endif

    public bool ReadErrorInfo(out ICanErrorInfo? errorInfo)
        => throw new NotSupportedException("Directly read error information not supported. Use ErrorOccured to receive error frames instead.");

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

    public event EventHandler<ICanErrorInfo>? ErrorOccurred
    {
        add
        {
            bool needStart = false;
            lock (_evtGate)
            {
                //TODO:未启用故障帧时的异常
                var before = _errorOccured;
                _errorOccured += value;
                if (!ReferenceEquals(before, _errorOccured))
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
                //TODO:未启用故障帧时的异常
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
        if (_isDisposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }


    private void StartReceiveLoopIfNeeded()
    {
        if (_pollTask != null) return;
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
#if NETSTANDARD2_0
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _recEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            var h = (uint)_recEvent.SafeWaitHandle.DangerousGetHandle().ToInt32();
            Api.SetValue(_handle, PcanParameter.ReceiveEvent, h);
        }
#else
#if WINDOWS
        _recEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        var h = (uint)_recEvent.SafeWaitHandle.DangerousGetHandle().ToInt32();
        Api.SetValue(_handle, PcanParameter.ReceiveEvent, h);
#endif
#endif
        _pollTask = Task.Run(() => PollLoop(token), token);
        CanKitLogger.LogDebug("PCAN: Poll loop started.");
    }

    private void StopReceiveLoop()
    {
        try
        {
#if NETSTANDARD2_0
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Api.SetValue(_handle, PcanParameter.ReceiveEvent, 0);
            }
#else
#if WINDOWS
            Api.SetValue(_handle, PcanParameter.ReceiveEvent, 0);
#endif
#endif
            _pollCts?.Cancel();
            _pollTask?.Wait(200);
        }
        catch { /*ignore*/ }
        finally
        {
            _pollTask = null;
            _pollCts?.Dispose();
            _pollCts = null;
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
        while (!token.IsCancellationRequested)
        {
            if (Volatile.Read(ref _subscriberCount) <= 0)
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


    private void DrainReceive()
    {
        while (true)
        {
            bool any = false;
            foreach (var rec in _transceiver.Receive(this, 16))
            {
                any = true;
                if (rec.CanFrame.IsErrorFrame)
                {
                    var raw = rec.CanFrame.RawID;
                    var span = rec.CanFrame.Data.Span;
                    var info = new DefaultCanErrorInfo(
                        PcanErr.ToFrameErrorType(raw, span),
                        CanKitExtension.ToControllerStatus(span[2], span[3]),
                        PcanErr.ToProtocolViolationType(raw, span),
                        PcanErr.ToErrorLocation(span),
                        DateTime.Now,
                        raw,
                        rec.ReceiveTimestamp,
                        PcanErr.ToDirection(span),
                        null,
                        PcanErr.ToTransceiverStatus(span),
                        PcanErr.ToErrorCounters(span),
                        rec.CanFrame);
                    _errorOccured?.Invoke(this, info);
                    continue;
                }

                var pred = _softwareFilterPredicate;
                if (!_useSoftwareFilter || pred is null || !pred(rec.CanFrame))
                {
                    _frameReceived?.Invoke(this, rec);
                    if (Volatile.Read(ref _asyncConsumerCount) > 0)
                        _asyncRx.Publish(rec);
                }
            }

            if (!any) break;
        }
    }

    private static Bitrate MapClassicBaud(CanBusTiming timing)
    {
        if (!timing.Classic!.Value.Nominal.IsTarget)
        {
            //TODO: 异常处理
            throw new Exception();
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
            _ => throw new CanChannelConfigurationException($"Unsupported PCAN classic bitrate: {b}")
        };
    }

    private static BitrateFD MapFdBitrate(CanBusTiming timing)
    {

        var (nomial, data, clockTmp) = timing.Fd!.Value;
        var clock = clockTmp ?? 80;
        // If advanced override is provided, build a custom FD string using same segments for both phases.
        BitrateFD.BitrateSegment nominalSeg = new BitrateFD.BitrateSegment();
        BitrateFD.BitrateSegment dataSeg = new BitrateFD.BitrateSegment();

        if (!Enum.IsDefined(typeof(BitrateFD.BitrateSegment), clock * 1_000_000))
        {
            //TODO:异常处理
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
            var segment = BitTimingSolver.FromSamplePoint(clock, bit, samplePoint);
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
            var segment = BitTimingSolver.FromSamplePoint(clock, bit, samplePoint);
            dataSeg.Tseg1 = segment.Tseg1;
            dataSeg.Tseg2 = segment.Tseg2;
            dataSeg.Brp = segment.Brp;
            dataSeg.Mode = BitrateFD.BitrateType.ArbitrationPhase;
            dataSeg.Sjw = segment.Sjw;
        }
        return new BitrateFD((BitrateFD.ClockFrequency)(clock * 1_000_000), nominalSeg, dataSeg);
    }

    private PcanChannel ParseHandle()
    {
        var s = Options.ChannelName!.Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            throw new CanBusCreationException("PCAN channel must not be empty.");
        }

        // PcanChannel.NoneBus
        if (s.Equals("PCAN_NONEBUS", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("NONEBUS", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("NONE", StringComparison.OrdinalIgnoreCase) ||
            s == "0")
            return 0;

        if (IsAllDigits(s))
        {
            if (int.TryParse(s, out var raw) && Enum.IsDefined(typeof(PcanChannel), raw))
                return (PcanChannel)raw;
            throw new CanBusCreationException($"Unknown PCAN channel value '{s}'.");
        }

        // Enum names: Usb01, Pci02
        if (Enum.TryParse<PcanChannel>(s, ignoreCase: true, out var named))
            return named;

        // PCAN names: PCAN_USBBUSn, PCAN_PCIBUSn, PCAN_LANBUSn
        var upper = s.ToUpperInvariant();

        PcanChannel FromIndex(string kind, int idx)
        {
            if (idx <= 0)
                throw new CanBusCreationException($"Channel index must start from 1 for {kind} (got {idx}).");
            var name = kind + idx.ToString("00"); // Usb01 / Pci02 / Lan12
            if (Enum.TryParse<PcanChannel>(name, ignoreCase: true, out var ch))
                return ch;
            throw new CanBusCreationException($"Unknown PCAN channel '{s}'.");
        }

        var m = System.Text.RegularExpressions.Regex.Match(upper, @"^(?:PCAN_)?USB(?:BUS)?(?<n>\d+)$");
        if (m.Success && int.TryParse(m.Groups["n"].Value, out var usbN))
            return FromIndex("Usb", usbN);

        m = System.Text.RegularExpressions.Regex.Match(upper, @"^(?:PCAN_)?PCI(?:BUS)?(?<n>\d+)$");
        if (m.Success && int.TryParse(m.Groups["n"].Value, out var pciN))
            return FromIndex("Pci", pciN);

        m = System.Text.RegularExpressions.Regex.Match(upper, @"^(?:PCAN_)?LAN(?:BUS)?(?<n>\d+)$");
        if (m.Success && int.TryParse(m.Groups["n"].Value, out var lanN))
            return FromIndex("Lan", lanN);

        throw new CanBusCreationException($"Unknown PCAN channel '{s}'.");

        static bool IsAllDigits(string t)
        {
            if (t.Any(ch => ch is < '0' or > '9'))
            {
                return false;
            }
            return t.Length > 0;
        }
    }

    private void SniffDynamicFeatures()
    {
        // Query channel features and merge supported ones
        if (Api.GetValue(_handle, PcanParameter.ChannelFeatures, out uint feature) == PcanStatus.OK)
        {
            var feats = (PcanDeviceFeatures)feature;
            CanFeature dyn = 0;
            if ((feats & PcanDeviceFeatures.FlexibleDataRate) != 0)
            {
                dyn |= CanFeature.CanFd;
            }

            Options.UpdateDynamicFeatures(dyn);
        }
    }
}
