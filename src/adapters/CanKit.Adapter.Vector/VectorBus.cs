using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CanKit.Adapter.Vector.Diagnostics;
using CanKit.Adapter.Vector.Native;
using CanKit.Adapter.Vector.Transceivers;
using CanKit.Adapter.Vector.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;
using Microsoft.Win32.SafeHandles;

namespace CanKit.Adapter.Vector;

public sealed class VectorBus : ICanBus<VectorBusRtConfigurator>
{
    private readonly object _evtGate = new();
    private readonly IVectorTransceiver _transceiver;
    private readonly AsyncFramePipe _asyncRx;
    private readonly IDisposable _driverScope;
    private CancellationTokenSource _pollCts;

    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<ICanErrorInfo>? _errorFrameReceived;
    private EventHandler<Exception>? _backgroundException;

    private Func<ICanFrame, bool>? _softwareFilterPredicate;

    private bool _isDisposed;
    private bool _isActivated;

    private readonly int _portHandle;
    private readonly ulong _accessMask;
    private readonly ManualResetEvent? _rxEvent;

    private BusState _busState;
    private CanErrorCounters _errorCounters;

    public bool IsDispose => _isDisposed;

    public VectorBusRtConfigurator Options { get; }

    public BusNativeHandle NativeHandle { get; }

    IBusRTOptionsConfigurator ICanBus.Options => Options;

    public BusState BusState => _busState;

    internal int Handle => _portHandle;
    internal ulong AccessMask => _accessMask;

    internal VectorBus(IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
    {
        _driverScope = VectorDriver.Acquire();
        Options = new VectorBusRtConfigurator();
        Options.Init((VectorBusOptions)options);
        _transceiver = (IVectorTransceiver)transceiver;
        _asyncRx = new AsyncFramePipe(Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null);

        var vectorOptions = (VectorBusOptions)options;
        var info = ((VectorProvider)provider).QueryChannelInfo(vectorOptions)
                   ?? throw new CanBusCreationException($"Vector channel index {vectorOptions.ChannelIndex} is not available.");

        _accessMask = info.ChannelMask;
        _pollCts = new CancellationTokenSource();
        UpdateCapabilities(options, info);

        ulong permissionMask = _accessMask;
        int handle = 0;

        try
        {
            VectorErr.ThrowIfError(VxlApi.xlOpenPort(ref handle, Options.ChannelName!, _accessMask, ref permissionMask, Options.ReceiveBufferCapacity,
                  (uint)(Options.ProtocolMode == CanProtocolMode.Can20 ? VxlApi.XL_INTERFACE_VERSION : VxlApi.XL_INTERFACE_VERSION_V4), VxlApi.XL_BUS_TYPE_CAN),
                "xlOpenPort", $"ChannelIndex={vectorOptions.ChannelIndex}");
            _portHandle = handle;
            NativeHandle = new BusNativeHandle(_portHandle);
            if (permissionMask != 0)
            {
                ApplyConfig(vectorOptions);
            }
            else
            {
                CanKitLogger.LogWarning("No permission to configure vector bus.");
            }

            ActivateChannel();

#if !FAKE
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use OS event notification to wake the RX loop instead of polling
                try
                {
                    VectorErr.ThrowIfError(
                        VxlApi.xlSetNotification(_portHandle, out var evHandle, 1),
                        "xlSetNotification");
                    _rxEvent = new ManualResetEvent(false)
                    {
                        SafeWaitHandle = new SafeWaitHandle(evHandle, false)
                    };
                }
                catch
                {
                    _rxEvent?.Dispose();
                    _rxEvent = null;
                    throw;
                }
            }
#endif
        }
        catch
        {
            if (_portHandle != 0)
            {
                try { VxlApi.xlClosePort(_portHandle); } catch { }
            }
            _driverScope.Dispose();
            throw;
        }
        Task.Run(() => RxDrainLoop(_pollCts.Token));
    }

    private void UpdateCapabilities(IBusOptions options, VectorChannelInfo info)
    {

        options.Capabilities = info.Capability;
        options.Features = info.Capability.Features;
    }

    private void ConfigureTiming(VectorBusOptions options)
    {
        if (options.ProtocolMode == CanProtocolMode.CanFd)
            ConfigureFdTiming(options);
        else
            ConfigureClassicTiming(options);
    }

    private void ConfigureClassicTiming(VectorBusOptions options)
    {
        var classic = options.BitTiming.Classic ?? new CanClassicTiming(CanPhaseTiming.Target(500_000u, 875), null);
        var bitrate = classic.Nominal.Bitrate ?? 500_000u;
        var samplePoint = (classic.Nominal.SamplePointPermille ?? 875) / 1000.0;
        var clock = classic.clockMHz ?? 16u;

        BitTimingSegments segments;
        if (classic.Nominal.Segments.HasValue)
        {
            segments = classic.Nominal.Segments.Value;
            if (classic.clockMHz.HasValue)
                bitrate = segments.BitRate(classic.clockMHz.Value);
        }
        else
        {
            segments = BitTimingSolver.FromSamplePoint(clock, bitrate, samplePoint);
        }

        var status = VxlApi.xlCanSetChannelBitrate(_portHandle, _accessMask, bitrate);
        if (status != VxlApi.XL_SUCCESS)
            VectorErr.ThrowIfError(status, "xlCanSetChannelBitrate");

        var chip = new VxlApi.XLchipParams
        {
            BitRate = bitrate,
            Sjw = ClampToByte(segments.Sjw, 1, 32),
            Tseg1 = ClampToByte(segments.Tseg1, 1, 255),
            Tseg2 = ClampToByte(segments.Tseg2, 1, 127),
            Sam = 1
        };

        status = VxlApi.xlCanSetChannelParams(_portHandle, _accessMask, ref chip);
        if (status != VxlApi.XL_SUCCESS)
            VectorErr.LogNonFatal(status, "xlCanSetChannelParams");
    }

    private void ConfigureFdTiming(VectorBusOptions options)
    {
        var fdTiming = options.BitTiming.Fd ?? new CanFdTiming(
            CanPhaseTiming.Target(500_000u, 800),
            CanPhaseTiming.Target(2_000_000u, 800),
            80);
        var clock = fdTiming.clockMHz ?? 80u;

        var nominalSeg = fdTiming.Nominal.Segments
            ?? BitTimingSolver.FromSamplePoint(clock, fdTiming.Nominal.Bitrate ?? 500_000u, GetSamplePoint(fdTiming.Nominal, 0.8));

        var dataSeg = fdTiming.Data.Segments
            ?? BitTimingSolver.FromSamplePoint(clock, fdTiming.Data.Bitrate ?? 2_000_000u, GetSamplePoint(fdTiming.Data, 0.8));

        var conf = new VxlApi.XLcanFdConf
        {
            ArbitrationBitRate = fdTiming.Nominal.Bitrate ?? nominalSeg.BitRate(clock),
            SjwAbr = nominalSeg.Sjw,
            Tseg1Abr = nominalSeg.Tseg1,
            Tseg2Abr = nominalSeg.Tseg2,
            DataBitRate = fdTiming.Data.Bitrate ?? dataSeg.BitRate(clock),
            SjwDbr = dataSeg.Sjw,
            Tseg1Dbr = dataSeg.Tseg1,
            Tseg2Dbr = dataSeg.Tseg2,
            Options = 0
        };

        VectorErr.ThrowIfError(VxlApi.xlCanFdSetConfiguration(_portHandle, _accessMask, ref conf), "xlCanFdSetConfiguration");
    }

    private static double GetSamplePoint(in CanPhaseTiming phase, double defaultValue)
        => (phase.SamplePointPermille ?? (ushort)(defaultValue * 1000)) / 1000.0;

    private void ConfigureOutputMode(ChannelWorkMode workMode)
    {
        int mode = workMode switch
        {
            ChannelWorkMode.ListenOnly => VxlApi.XL_OUTPUT_MODE_SILENT,
            _ => VxlApi.XL_OUTPUT_MODE_NORMAL
        };

        var status = VxlApi.xlCanSetChannelOutput(_portHandle, _accessMask, mode);
        if (status != VxlApi.XL_SUCCESS)
            VectorErr.LogNonFatal(status, "xlCanSetChannelOutput");
    }

    private void ApplyHardwareFilters(VectorBusOptions options)
    {
        VectorErr.ThrowIfError(VxlApi.xlCanResetAcceptance(_portHandle, _accessMask, 1), "xlCanResetAcceptance(STD)");
        VectorErr.ThrowIfError(VxlApi.xlCanResetAcceptance(_portHandle, _accessMask, 2), "xlCanResetAcceptance(EXT)");
        bool stdRest = false;
        bool extRest = false;
        foreach (var rule in options.Filter.filterRules)
        {
            if (rule is FilterRule.Mask mask)
            {

                uint type = mask.FilterIdType == CanFilterIDType.Extend ? 2u : 1u;
                if (type == 1 && !stdRest)
                {
                    VectorErr.ThrowIfError(VxlApi.xlCanSetChannelAcceptance(_portHandle, _accessMask, 0xFFF, 0xFFF, 1),
                        "xlCanSetChannelAcceptance(STD)");
                    stdRest = true;
                }

                if (type == 2 && !extRest)
                {
                    extRest = true;
                    VectorErr.ThrowIfError(VxlApi.xlCanSetChannelAcceptance(_portHandle, _accessMask, 0xFFFFFFFF, 0xFFFFFFFF, 2),
                        "xlCanSetChannelAcceptance(STD)");
                }

                VectorErr.ThrowIfError(
                    VxlApi.xlCanSetChannelAcceptance(_portHandle, _accessMask, mask.AccCode, mask.AccMask, type),
                    "xlCanSetChannelAcceptance",
                    $"Type={(type == 1 ? "STD" : "EXT")}, Code=0x{mask.AccCode:X}, Mask=0x{mask.AccMask:X}");
            }
            else if (rule is FilterRule.Range range) /*Only std*/
            {
                if (!stdRest)
                {
                    VectorErr.ThrowIfError(VxlApi.xlCanSetChannelAcceptance(_portHandle, _accessMask, 0xFFF, 0xFFF, 1),
                        "xlCanSetChannelAcceptance(STD)");
                    stdRest = true;
                }
                VectorErr.ThrowIfError(VxlApi.xlCanAddAcceptanceRange(_portHandle, _accessMask, range.From, range.To),
                    "xlCanAddAcceptanceRange(STD)");
            }
        }
    }

    private void ActivateChannel()
    {
        VectorErr.ThrowIfError(VxlApi.xlActivateChannel(_portHandle, _accessMask, VxlApi.XL_BUS_TYPE_CAN, 0), "xlActivateChannel");
        _isActivated = true;
    }

    public void ApplyConfig(VectorBusOptions options)
    {
        ConfigureTiming(options);
        ConfigureOutputMode(options.WorkMode);
        ApplyHardwareFilters(options);
        _softwareFilterPredicate = FilterRule.Build(options.Filter.SoftwareFilterRules);
        Console.WriteLine(options.Filter.SoftwareFilterRules.Count);
    }

    public void Reset()
    {
        ThrowIfDisposed();
        if (_isActivated)
        {
            VectorErr.ThrowIfError(VxlApi.xlDeactivateChannel(_portHandle, _accessMask), "xlDeactivateChannel");
            VectorErr.ThrowIfError(VxlApi.xlActivateChannel(_portHandle, _accessMask, VxlApi.XL_BUS_TYPE_CAN, 0), "xlActivateChannel");
        }
    }

    public void ClearBuffer()
    {
        ThrowIfDisposed();
        VectorErr.ThrowIfError(VxlApi.xlFlushReceiveQueue(_portHandle), "xlFlushReceiveQueue");
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

    public int Transmit(ICanFrame[] frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames.AsSpan());
    }

    public int Transmit(ArraySegment<ICanFrame> frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames.AsSpan());
    }


    public int Transmit(in ICanFrame frame)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frame);
    }

    public Task<int> TransmitAsync(IEnumerable<ICanFrame> frames, int timeOut = 0, CancellationToken cancellationToken = default)
        => Task.FromResult(Transmit(frames, timeOut));

    public Task<int> TransmitAsync(ICanFrame frame, CancellationToken cancellationToken = default)
        => Task.FromResult(Transmit(in frame));

    public IPeriodicTx TransmitPeriodic(ICanFrame frame, PeriodicTxOptions options)
    {
        ThrowIfDisposed();
        if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) != 0)
            return SoftwarePeriodicTx.Start(this, frame, options);
        throw new CanFeatureNotSupportedException(CanFeature.CyclicTx, Options.Features);
    }

    public float BusUsage()
    {
        ThrowIfDisposed();
        throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Features);
    }

    public IEnumerable<CanReceiveData> Receive(int count = 1, int timeout = 0)
    {
        return ReceiveAsync(count, timeout).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return await _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken).ConfigureAwait(false);
    }

#if NET8_0_OR_GREATER
    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await foreach (var frame in _asyncRx.ReadAllAsync(cancellationToken))
        {
            yield return frame;
        }
    }
#endif

    public event EventHandler<CanReceiveData> FrameReceived
    {
        add
        {
            ThrowIfDisposed();
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

    public event EventHandler<ICanErrorInfo> ErrorFrameReceived
    {
        add
        {
            ThrowIfDisposed();
            lock (_evtGate)
            {
                _errorFrameReceived += value;
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _errorFrameReceived -= value;
            }
        }
    }

    public event EventHandler<Exception> BackgroundExceptionOccurred
    {
        add
        {
            ThrowIfDisposed();
            lock (_evtGate)
            {
                _backgroundException += value;
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _backgroundException -= value;
            }
        }
    }

    public CanErrorCounters ErrorCounters()
    {
        ThrowIfDisposed();
        return _errorCounters;
    }


    public void RequestBusState()
    {
        VectorErr.ThrowIfError(VxlApi.xlCanRequestChipState(_portHandle, _accessMask), "xlCanRequestChipState()");
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            if (_isActivated)
                VectorErr.ThrowIfError(VxlApi.xlDeactivateChannel(_portHandle, _accessMask), "xlDeactivateChannel");
        }
        catch (Exception ex)
        {
            CanKitLogger.LogWarning("Vector: failed to deactivate channel during dispose.", ex);
        }

        try
        {
            VectorErr.ThrowIfError(VxlApi.xlClosePort(_portHandle), "xlClosePort");
        }
        catch (VectorNativeException ex)
        {
            CanKitLogger.LogWarning($"Vector: xlClosePort failed ({ex.ErrorText}).");
        }
        finally
        {
            _rxEvent?.Dispose();
            _driverScope.Dispose();
        }
    }




    private void RxDrainLoop(CancellationToken token)
    {
        try
        {
            List<CanReceiveData> receiveData = new(VxlApi.RX_BATCH_COUNT);
            List<ICanErrorInfo> errInfos = new(VxlApi.RX_BATCH_COUNT);
            while (!_isDisposed)
            {
                if (_rxEvent != null)
                {
                    var handles = new[] { _rxEvent, token.WaitHandle };
                    WaitHandle.WaitAny(handles);
                }

                if (token.IsCancellationRequested)
                    break;

                while (true)
                {
                    while (_transceiver.ReceiveEvents(this, receiveData, errInfos))
                    {
                        foreach (var data in receiveData)
                        {
                            if (_softwareFilterPredicate is null || !data.CanFrame.IsExtendedFrame || _softwareFilterPredicate(data.CanFrame))
                            {
                                _frameReceived?.Invoke(this, data);
                                _asyncRx.Publish(data);
                            }
                        }

                        foreach (var errInfo in errInfos)
                        {
                            if (errInfo.Type == FrameErrorType.Controller)
                            {
                                _busState = errInfo.RawErrorCode switch
                                {
                                    VxlApi.XL_CHIPSTAT_BUSOFF => BusState.BusOff,
                                    VxlApi.XL_CHIPSTAT_ERROR_PASSIVE => BusState.ErrPassive,
                                    VxlApi.XL_CHIPSTAT_ERROR_WARNING => BusState.ErrWarning,
                                    VxlApi.XL_CHIPSTAT_ERROR_ACTIVE => BusState.ErrActive,
                                    _ => BusState.None
                                };
                                _errorCounters = errInfo.ErrorCounters!.Value;
                            }
                            else
                            {
                                _errorFrameReceived?.Invoke(this, errInfo);
                            }

                        }
                        receiveData.Clear();
                        errInfos.Clear();
                    }
                    if (_rxEvent == null)
                    {
                        PreciseDelay.Delay(TimeSpan.FromMilliseconds(Options.PollingInterval));
                    }
                    else
                    {
                        _rxEvent.Reset();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _backgroundException?.Invoke(this, ex);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new CanBusDisposedException();
    }

    private static byte ClampToByte(uint value, byte min, byte max)
    {
        var clamped = Math.Min(Math.Max(value, min), max);
        return (byte)clamped;
    }
}
