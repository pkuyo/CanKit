
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Peak.Can.Basic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.Core.Diagnostics;
using Pkuyo.CanKit.Net.Core.Utils;

namespace Pkuyo.CanKit.Net.PCAN;

public sealed class PcanBus : ICanBus<PcanBusRtConfigurator>, ICanApplier, IBusOwnership
{


    internal PcanBus(IBusOptions options, ITransceiver transceiver)
    {
        Options = new PcanBusRtConfigurator();
        Options.Init((PcanBusOptions)options);
        var options1 = (PcanBusOptions)options;
        _transceiver = transceiver;

        _handle = ParseHandle(options1.Channel);

        // Discover runtime capabilities (e.g., FD) and merge to dynamic features
        SniffDynamicFeatures();
        CanKitLogger.LogInformation($"PCAN: Initializing on '{options1.Channel}', Mode={Options.ProtocolMode}, Features={Options.Features}");

        // If requested FD but not supported at runtime, fail early
        if (Options.ProtocolMode == CanProtocolMode.CanFd && (Options.Features & CanFeature.CanFd) == 0)
        {
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, Options.Features);
        }

        // Initialize according to selected protocol mode
        if (Options.ProtocolMode == CanProtocolMode.CanFd)
        {
            var fd = MapFdBitrate(Options.BitTiming);
            var st = Api.Initialize(_handle, fd);
            if (st != PcanStatus.OK)
            {
                throw new CanBusCreationException($"PCAN InitializeFD failed: {st}");
            }
            CanKitLogger.LogInformation("PCAN: InitializeFD succeeded.");
        }
        else if (Options.ProtocolMode == CanProtocolMode.Can20)
        {
            var baud = MapClassicBaud(Options.BitTiming);
            var st = Api.Initialize(_handle, baud);
            if (st != PcanStatus.OK)
            {
                throw new CanBusCreationException($"PCAN Initialize failed: {st}");
            }
            CanKitLogger.LogInformation("PCAN: Initialize (classic) succeeded.");
        }
        else
        {
            throw new CanFeatureNotSupportedException(CanFeature.MergeReceive, Options.Features);
        }

        // Apply initial options (filters etc.)
        options1.Apply(this, true);
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
        if ((Options.EnabledSoftwareFallbackE & CanFeature.CyclicTx) != 0)
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

    public bool ReadErrorInfo(out ICanErrorInfo? errorInfo)
    {
        errorInfo = null;
        return false;
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
            StopPolling();
            _ = Api.Uninitialize(_handle);
        }
        finally
        {
            _isDisposed = true;
            _owner?.Dispose();
        }
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
        }
        if (pc.AllowErrorInfo)
        {
            Api.SetValue(_handle, PcanParameter.AllowErrorFrames, ParameterValue.Activation.On);
        }
    }

    public CanOptionType ApplierStatus => CanOptionType.Runtime;

    public event EventHandler<CanReceiveData>? FrameReceived
    {
        add
        {
            lock (_evtGate)
            {
                _frameReceived += value;
                _subscriberCount++;
                StartPollingIfNeeded();
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _frameReceived -= value;
                _subscriberCount = Math.Max(0, _subscriberCount - 1);
                if (_subscriberCount == 0) StopPolling();
            }
        }
    }

    public event EventHandler<ICanErrorInfo>? ErrorOccurred
    {
        add => throw new CanFeatureNotSupportedException(CanFeature.ErrorFrame, Options.Features);
        remove => throw new CanFeatureNotSupportedException(CanFeature.ErrorFrame, Options.Features);
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


    public PcanStatus PCanState => Api.GetStatus(_handle);

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }


    private void StartPollingIfNeeded()
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

    private void StopPolling()
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
        catch { }
        finally
        {
            _pollTask = null;
            _pollCts?.Dispose();
            _pollCts = null;
            CanKitLogger.LogDebug("PCAN: Poll loop stopped.");
        }
    }

    private void PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (Volatile.Read(ref _subscriberCount) <= 0)
            {
                break;
            }
            _recEvent.WaitOne();
            foreach (var rec in _transceiver.Receive(this, 16))
            {
                var useSw = (Options.EnabledSoftwareFallbackE & CanFeature.Filters) != 0
                            && Options.Filter.SoftwareFilterRules.Count > 0;
                if (useSw)
                {
                    var pred = FilterRule.Build(Options.Filter.SoftwareFilterRules);
                    if (!pred(rec.CanFrame)) continue;
                }
                _frameReceived?.Invoke(this, rec);
            }
        }
    }

    private static Bitrate MapClassicBaud(BitTiming timing)
    {
        var b = timing.BaudRate ?? 500_000u;
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

    private static BitrateFD MapFdBitrate(BitTiming timing)
    {
        var abit = timing.ArbitrationBitRate ?? 500_000u;
        var dbit = timing.DataBitRate ?? 2_000_000u;

        // Common 80MHz-based presets from PCAN documentation
        if (abit == 500_000 && dbit == 2_000_000)
            return new BitrateFD("f_clock_mhz=80, nom_brp=4, nom_tseg1=63, nom_tseg2=16, nom_sjw=16, data_brp=2, data_tseg1=16, data_tseg2=7, data_sjw=7");
        if (abit == 500_000 && dbit == 1_000_000)
            return new BitrateFD("f_clock_mhz=80, nom_brp=4, nom_tseg1=63, nom_tseg2=16, nom_sjw=16, data_brp=4, data_tseg1=16, data_tseg2=7, data_sjw=7");
        if (abit == 250_000 && dbit == 2_000_000)
            return new BitrateFD("f_clock_mhz=80, nom_brp=8, nom_tseg1=63, nom_tseg2=16, nom_sjw=16, data_brp=2, data_tseg1=16, data_tseg2=7, data_sjw=7");
        if (abit == 250_000 && dbit == 1_000_000)
            return new BitrateFD("f_clock_mhz=80, nom_brp=8, nom_tseg1=63, nom_tseg2=16, nom_sjw=16, data_brp=4, data_tseg1=16, data_tseg2=7, data_sjw=7");

        // Fallback to a safe default
        return new BitrateFD("f_clock_mhz=80, nom_brp=4, nom_tseg1=63, nom_tseg2=16, nom_sjw=16, data_brp=2, data_tseg1=16, data_tseg2=7, data_sjw=7");
    }

    private static PcanChannel ParseHandle(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new CanBusCreationException("PCAN channel must not be empty.");
        }

        var s = channel.Trim();

        // If given as integer code (e.g., 1281), try direct parse
        if (int.TryParse(s, out var raw) && Enum.IsDefined(typeof(PcanChannel), raw))
        {
            return (PcanChannel)raw;
        }

        // Normalize common PCAN names: PCAN_USBBUSn, PCAN_PCIBUSn, PCAN_LANBUSn
        var upper = s.ToUpperInvariant();

        PcanChannel FromIndex(string kind, int index)
        {
            var name = kind + index.ToString("00");
            if (Enum.TryParse<PcanChannel>(name, ignoreCase: true, out var ch))
                return ch;
            throw new CanBusCreationException($"Unknown PCAN channel '{channel}'.");
        }

        var m = System.Text.RegularExpressions.Regex.Match(upper, @"^(PCAN_)?USB(BUS)?(?<n>\d+)$");
        if (m.Success && int.TryParse(m.Groups["n"].Value, out var usbIdx))
        {
            return FromIndex("Usb", Math.Max(1, usbIdx));
        }

        m = System.Text.RegularExpressions.Regex.Match(upper, @"^(PCAN_)?PCI(BUS)?(?<n>\d+)$");
        if (m.Success && int.TryParse(m.Groups["n"].Value, out var pciIdx))
        {
            return FromIndex("Pci", Math.Max(1, pciIdx));
        }

        m = System.Text.RegularExpressions.Regex.Match(upper, @"^(PCAN_)?LAN(BUS)?(?<n>\d+)$");
        if (m.Success && int.TryParse(m.Groups["n"].Value, out var lanIdx))
        {
            return FromIndex("Lan", Math.Max(1, lanIdx));
        }

        // Accept already-enum-like names, e.g., Usb01, Pci02
        if (Enum.TryParse<PcanChannel>(s, ignoreCase: true, out var parsed))
            return parsed;

        throw new CanBusCreationException($"Unknown PCAN channel '{channel}'.");
    }

    private void SniffDynamicFeatures()
    {
        // Query channel features and merge supported ones
        if (Api.GetValue(_handle, PcanParameter.ChannelFeatures, out uint feature) == PcanStatus.OK)
        {
            var feats = (PcanDeviceFeatures)feature;
            var dyn = CanFeature.CanClassic | CanFeature.Filters;
            if ((feats & PcanDeviceFeatures.FlexibleDataRate) != 0)
            {
                dyn |= CanFeature.CanFd;
            }

            Options.UpdateDynamicFeatures(dyn);
        }
        else
        {
            // Fallback: assume classic + filters
            Options.UpdateDynamicFeatures(CanFeature.CanClassic | CanFeature.Filters);
        }
    }

    public void AttachOwner(IDisposable owner)
    {
        _owner = owner;
    }

    private readonly ITransceiver _transceiver;
    private bool _isDisposed;
    private readonly object _evtGate = new();
    private EventHandler<CanReceiveData>? _frameReceived;
    //private EventHandler<ICanErrorInfo>? _errorOccurred;

    private int _subscriberCount;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private EventWaitHandle _recEvent;

    private IDisposable? _owner;

    internal PcanChannel Handle => _handle;

    private readonly PcanChannel _handle;
}
