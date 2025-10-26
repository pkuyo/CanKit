using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CanKit.Adapter.Vector.Diagnostics;
using CanKit.Adapter.Vector.Native;
using CanKit.Adapter.Vector.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;

namespace CanKit.Adapter.Vector;

public sealed class VectorBus : ICanBus<VectorBusRtConfigurator>
{
    private readonly object _evtGate = new();
    private readonly ITransceiver _transceiver;
    private readonly AsyncFramePipe _asyncRx;
    private readonly IDisposable _driverScope;

    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<ICanErrorInfo>? _errorFrameReceived;
    private EventHandler<Exception>? _backgroundException;

    private Func<ICanFrame, bool>? _softwareFilterPredicate;

    private CancellationTokenSource? _stopDelayCts;
    private int _asyncConsumerCount;
    private int _subscriberCount;
    private int _asyncBufferingLinger;
    private int _drainRunning;
    private bool _isDisposed;
    private bool _isActivated;

    private readonly int _portHandle;
    private readonly ulong _accessMask;
    private readonly AutoResetEvent? _rxEvent;

    public VectorBusRtConfigurator Options { get; }

    public BusNativeHandle NativeHandle { get; }

    IBusRTOptionsConfigurator ICanBus.Options => Options;

    public BusState BusState => BusState.None;

    internal int Handle => _portHandle;
    internal ulong AccessMask => _accessMask;

    internal VectorBus(IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
    {
        _driverScope = VectorDriver.Acquire();
        Options = new VectorBusRtConfigurator();
        Options.Init((VectorBusOptions)options);
        _transceiver = transceiver;
        _asyncRx = new AsyncFramePipe(Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null);

        var vectorOptions = (VectorBusOptions)options;
        var info = ((VectorProvider)provider).QueryChannelInfo(vectorOptions)
                   ?? throw new CanBusCreationException($"Vector channel index {vectorOptions.ChannelIndex} is not available.");

        _accessMask = info.ChannelMask;

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
        foreach (var rule in options.Filter.filterRules)
        {
            if (rule is FilterRule.Mask mask)
            {

                uint type = mask.FilterIdType == CanFilterIDType.Extend ? 2u : 1u;
                if (type == 1 && !stdRest)
                {
                    VectorErr.ThrowIfError(VxlApi.xlCanSetChannelAcceptance(_portHandle, _accessMask, 0xFFF, 0xFFF, 1),
                        "xlCanResetAcceptance(STD)");
                    stdRest = true;
                }

                VectorErr.ThrowIfError(
                    VxlApi.xlCanSetChannelAcceptance(_portHandle, _accessMask, mask.AccCode, mask.AccMask, type),
                    "xlCanSetChannelAcceptance",
                    $"Type={(type == 1 ? "STD" : "EXT")}, Code=0x{mask.AccCode:X}, Mask=0x{mask.AccMask:X}");
            }
            else if (rule is FilterRule.Range range) /*Only std*/
            {
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
        if((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) != 0)
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

    internal IEnumerable<CanReceiveData> ReceiveInternal(int count = 1)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        int emitted = 0;

        while (count == 0 || emitted < count)
        {
            var status = VxlApi.xlCanReceive(_portHandle, out var nativeEvent);
            if (status == VxlApi.XL_ERR_QUEUE_IS_EMPTY)
            {
                yield break;
            }

            VectorErr.ThrowIfError(status, "xlCanReceive");

            if (TryProcessEvent(nativeEvent, out var frame, out var errorInfo))
            {
                if (_softwareFilterPredicate == null || _softwareFilterPredicate(frame.CanFrame))
                {
                    emitted++;
                    yield return frame;
                }
            }
            else if (errorInfo != null)
            {
                _errorFrameReceived?.Invoke(this, errorInfo);
            }
        }
    }

    public async Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _subscriberCount);
        Interlocked.Increment(ref _asyncConsumerCount);
        Volatile.Write(ref _asyncBufferingLinger, 0);
        StartRxLoopIfNeed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
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
                RequestStopRxLoop();
            }
        }
    }

#if NET8_0_OR_GREATER
    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        StartRxLoopIfNeed();
        Interlocked.Increment(ref _asyncConsumerCount);
        try
        {
            await foreach (var frame in _asyncRx.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _asyncConsumerCount);
            RequestStopRxLoop();
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
                _subscriberCount++;
                StartRxLoopIfNeed();
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _frameReceived -= value;
                _subscriberCount = Math.Max(0, _subscriberCount - 1);
                RequestStopRxLoop();
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
                StartRxLoopIfNeed();
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _errorFrameReceived -= value;
                RequestStopRxLoop();
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
                StartRxLoopIfNeed();
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _backgroundException -= value;
                RequestStopRxLoop();
            }
        }
    }

    public CanErrorCounters ErrorCounters()
    {
        ThrowIfDisposed();
        return new CanErrorCounters();
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
            try { _rxEvent?.Set(); } catch { }
            _rxEvent?.Dispose();
            _driverScope.Dispose();
        }
    }

    private void StartRxLoopIfNeed()
    {
        if (Volatile.Read(ref _drainRunning) == 1 || _isDisposed)
            return;
        if (Interlocked.Exchange(ref _drainRunning, 1) == 1)
            return;
        Task.Run(RxDrainLoop);
    }

    private void RequestStopRxLoop()
    {
        if (_subscriberCount <= 0 && _asyncConsumerCount <= 0)
        {
            _stopDelayCts?.Cancel();
            var cts = new CancellationTokenSource();
            _stopDelayCts = cts;
            Task.Delay(Options.ReceiveLoopStopDelayMs).ContinueWith(_ =>
            {
                if (!cts.IsCancellationRequested)
                    Interlocked.Exchange(ref _drainRunning, 0);
            }, TaskScheduler.Default);
        }
    }

    private void RxDrainLoop()
    {
        try
        {
            while (Volatile.Read(ref _drainRunning) == 1 && !_isDisposed)
            {
                if (_rxEvent != null)
                {
                    _rxEvent.WaitOne(Options.PollingInterval);
                }

                while (true)
                {
                    var status = VxlApi.xlCanReceive(_portHandle, out var nativeEvent);
                    if (status == VxlApi.XL_ERR_QUEUE_IS_EMPTY)
                    {
                        if (_rxEvent == null)
                            Thread.Sleep(Options.PollingInterval);
                        break;
                    }
                    VectorErr.ThrowIfError(status, "xlCanReceive(loop)");

                    if (TryProcessEvent(nativeEvent, out var frame, out var errorInfo))
                    {
                        if (_softwareFilterPredicate == null || _softwareFilterPredicate(frame.CanFrame))
                        {
                            _asyncRx.Publish(frame);
                            _frameReceived?.Invoke(this, frame);
                        }
                    }
                    else if (errorInfo != null)
                    {
                        _errorFrameReceived?.Invoke(this, errorInfo);
                    }

                    if (_rxEvent == null)
                        break; // Linux polling path processes one event per loop
                }
            }
        }
        catch (Exception ex)
        {
            _backgroundException?.Invoke(this, ex);
        }
    }

    private bool TryProcessEvent(in VxlApi.XLcanRxEvent nativeEvent, out CanReceiveData data, out ICanErrorInfo? errorInfo)
    {
        data = default;
        errorInfo = null;

        switch (nativeEvent.Tag)
        {
            case VxlApi.XL_CAN_EV_TAG_RX_OK:
                data = BuildReceiveData(nativeEvent.TagData.CanRxOkMsg);
                return true;

            case VxlApi.XL_CAN_EV_TAG_TX_OK:
                return false;

            case VxlApi.XL_CAN_EV_TAG_CHIP_STATE:
                errorInfo = BuildChipStateError(nativeEvent.TagData.ChipState);
                return false;

            case VxlApi.XL_CAN_EV_TAG_RX_ERROR:
            case VxlApi.XL_CAN_EV_TAG_TX_ERROR:
                errorInfo = BuildGenericError(nativeEvent.Tag, nativeEvent.TagData);
                return false;

            default:
                return false;
        }
    }

    private static CanReceiveData BuildReceiveData(in VxlApi.XLcanRxMsg msg)
    {
        var isExtended = (msg.CanId & VxlApi.XL_CAN_EXT_MSG_ID) != 0;
        var flags = msg.MsgFlags;
        var isFd = (flags & VxlApi.XL_CAN_RXMSG_FLAG_EDL) != 0;

        var length = isFd ? CanFdFrame.DlcToLen(msg.Dlc) : Math.Min(msg.Dlc, (byte)8);
        var payload = new byte[length];

        unsafe
        {
            fixed (byte* src = msg.Data)
            fixed (byte* dest = payload)
            {
                Unsafe.CopyBlockUnaligned(src, dest, (uint)length);
            }
        }

        if (isFd)
        {
            var frame = new CanFdFrame((int)(msg.CanId & 0x1FFFFFFF), payload,
                (flags & VxlApi.XL_CAN_RXMSG_FLAG_BRS) != 0,
                (flags & VxlApi.XL_CAN_RXMSG_FLAG_ESI) != 0,
                isExtended)
            { IsErrorFrame = (flags & VxlApi.XL_CAN_RXMSG_FLAG_EF) != 0 };
            return new CanReceiveData(frame);
        }
        else
        {
            var frame = new CanClassicFrame((int)(msg.CanId & 0x1FFFFFFF), payload)
            {
                IsExtendedFrame = isExtended,
                IsRemoteFrame = (flags & VxlApi.XL_CAN_RXMSG_FLAG_RTR) != 0,
                IsErrorFrame = (flags & VxlApi.XL_CAN_RXMSG_FLAG_EF) != 0
            };
            return new CanReceiveData(frame);
        }
    }

    private static ICanErrorInfo BuildChipStateError(in VxlApi.XLchipState state)
    {
        CanControllerStatus controllerStatus;
        var busStatus = state.BusStatus;

        if ((busStatus & VxlApi.XL_CHIPSTAT_BUSOFF) != 0)
            controllerStatus = CanControllerStatus.TxPassive | CanControllerStatus.RxPassive;
        else if ((busStatus & VxlApi.XL_CHIPSTAT_ERROR_PASSIVE) != 0)
            controllerStatus = CanControllerStatus.TxPassive | CanControllerStatus.RxPassive;
        else if ((busStatus & VxlApi.XL_CHIPSTAT_ERROR_WARNING) != 0)
            controllerStatus = CanControllerStatus.TxWarning | CanControllerStatus.RxWarning;
        else
            controllerStatus = CanControllerStatus.Active;

        var counters = new CanErrorCounters
        {
            TransmitErrorCounter = state.TxErrorCounter,
            ReceiveErrorCounter = state.RxErrorCounter
        };

        return new DefaultCanErrorInfo(
            FrameErrorType.Controller,
            controllerStatus,
            CanProtocolViolationType.None,
            FrameErrorLocation.Unspecified,
            DateTime.UtcNow,
            state.BusStatus,
            null,
            FrameDirection.Unknown,
            null,
            CanTransceiverStatus.Unspecified,
            counters,
            null);
    }

    private static ICanErrorInfo BuildGenericError(ushort tag, in VxlApi.XLcanRxTagData data)
    {
        byte errorCode;
        try
        {
            errorCode = data.CanError.ErrorCode;
        }
        catch
        {
            errorCode = (byte)(tag & 0xFF);
        }

        return new DefaultCanErrorInfo(
            FrameErrorType.BusError,
            CanControllerStatus.Unknown,
            CanProtocolViolationType.Unknown,
            FrameErrorLocation.Unspecified,
            DateTime.UtcNow,
            errorCode,
            null,
            FrameDirection.Unknown,
            null,
            CanTransceiverStatus.Unspecified,
            null,
            null);
    }

    private static VxlApi.XLcanRxEvent CreateReceiveEvent()
        => new VxlApi.XLcanRxEvent { Size = VxlApi.SizeOfCanRxEvent };

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
