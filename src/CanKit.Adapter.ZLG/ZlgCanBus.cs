using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Diagnostics;
using CanKit.Adapter.ZLG.Native;
using CanKit.Adapter.ZLG.Options;
using CanKit.Adapter.ZLG.Transceivers;
using CanKit.Adapter.ZLG.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;

namespace CanKit.Adapter.ZLG
{

    public sealed class ZlgCanBus : ICanBus<ZlgBusRtConfigurator>, IBusOwnership
    {
        private readonly HashSet<int> _autoSendIndexes = new();

        private readonly IntPtr _devicePtr;

        private readonly object _evtGate = new();

        private readonly ZlgChannelHandle _nativeHandle;

        private readonly IZlgTransceiver _transceiver;

        private EventHandler<ICanErrorInfo>? _errorOccurred;

        private EventHandler<CanReceiveData>? _frameReceived;

        private bool _isDisposed;

        private IDisposable? _owner;

        private CancellationTokenSource? _pollCts;

        private Task? _pollTask;
        private Func<ICanFrame, bool>? _softwareFilterPredicate;

        private int _subscriberCount;
        private bool _useSoftwareFilter;
        private readonly AsyncFramePipe _asyncRx;
        private int _asyncConsumerCount;
        private CancellationTokenSource? _stopDelayCts;

        internal ZlgCanBus(ZlgCanDevice device, IBusOptions options, ITransceiver transceiver)
        {
            _devicePtr = device.NativeHandler.DangerousGetHandle();

            Options = new ZlgBusRtConfigurator();
            Options.Init((ZlgBusOptions)options);
            _asyncRx = new AsyncFramePipe(Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null);
            CanKitLogger.LogInformation($"ZLG: Initializing channel '{Options.ChannelName?? Options.ChannelIndex.ToString()}', Mode={Options.ProtocolMode}...");
            ApplyConfig(options);
            ZLGCAN.ZCAN_SetValue(_devicePtr, options.ChannelIndex+"clear_auto_send", "0");
            var provider = Options.Provider as ZlgCanProvider;

            if (provider is null)
            {
                CanKitLogger.LogWarning($"provider is not ZlgCanProvider, Device={device.Options.DeviceType}, DeviceIndex={device.Options.DeviceIndex}, ChannelIndex={options.ChannelIndex}");
            }

            ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG config = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG
            {
                can_type = options.ProtocolMode == CanProtocolMode.Can20 ? 0U : 1U,
            };
            config.config.can.mode = (byte)options.WorkMode;
            CanKitLogger.LogInformation($"ZLG: Initializing on '{options.ChannelIndex}', Mode={options.ProtocolMode}, Features={Options.Features}");

            if (options.ProtocolMode == CanProtocolMode.CanFd)
            {
                var arbitrationRate = options.BitTiming.Fd?.Nominal.Bitrate
                                      ?? throw new CanBusConfigurationException("Arbitration bitrate must be specified when configuring CAN FD timing.");
                var dataRate = options.BitTiming.Fd?.Data.Bitrate
                               ?? throw new CanBusConfigurationException("Data bitrate must be specified when configuring CAN FD timing.");
                config.config.canfd.abit_timing = arbitrationRate;
                config.config.canfd.dbit_timing = dataRate;
            }

            if (options.Filter.filterRules.Count > 0)
            {
                if (options.Filter.filterRules[0] is FilterRule.Mask mask)
                {
                    config.config.can.acc_code = mask.AccCode;
                    config.config.can.acc_mask = mask.AccMask;
                    config.config.can.filter = (byte)mask.FilterIdType;

                }
                else
                {
                    config.config.can.acc_code = 0;
                    config.config.can.acc_mask = 0xffffffff;
                }

            }

            var handle = ZLGCAN.ZCAN_InitCAN(device.NativeHandler, (uint)Options.ChannelIndex, ref config);
            CanKitLogger.LogInformation("ZLG: Initialize succeeded.");
            handle.SetDevice(device.NativeHandler.DangerousGetHandle());
            _nativeHandle = handle;
            ZlgErr.ThrowIfInvalid(handle, nameof(ZLGCAN.ZCAN_InitCAN));
            Reset();
            ApplyConfigAfterInit(options);

            ZlgErr.ThrowIfError(ZLGCAN.ZCAN_StartCAN(_nativeHandle), nameof(ZLGCAN.ZCAN_StartCAN), _nativeHandle);
            if (transceiver is not IZlgTransceiver zlg)
                throw new CanTransceiverMismatchException(typeof(IZlgTransceiver), transceiver.GetType());
            _transceiver = zlg;
            CanKitLogger.LogDebug("ZLG: Initial options applied.");
        }

        public ZlgChannelHandle NativeHandle => _nativeHandle;

        public void AttachOwner(IDisposable owner)
        {
            _owner = owner;
        }


        public void Reset()
        {
            ThrowIfDisposed();
            ZlgErr.ThrowIfError(ZLGCAN.ZCAN_ResetCAN(_nativeHandle), nameof(ZLGCAN.ZCAN_ResetCAN), _nativeHandle);
        }


        public void ClearBuffer()
        {
            ThrowIfDisposed();
            ZlgErr.ThrowIfError(ZLGCAN.ZCAN_ClearBuffer(_nativeHandle), nameof(ZLGCAN.ZCAN_ClearBuffer), _nativeHandle);
        }

        public int Transmit(IEnumerable<ICanFrame> frames, int timeOut = 0)
        {
            ThrowIfDisposed();
            try
            {
                var list = frames.ToArray();
                bool isFirst = true;
                int result = 0;
                var startTime = Environment.TickCount;
                do
                {
                    if (isFirst)
                        isFirst = false;
                    else
                        Thread.Sleep(Math.Min(Environment.TickCount - startTime, Options.PollingInterval));

                    var count = _transceiver.Transmit(this, new ArraySegment<ICanFrame>(list, result, list.Length - result));
                    result += count;
                } while (result < list.Length && Environment.TickCount - startTime <= timeOut);

                return result;
            }
            catch (Exception ex)
            {
                HandleBackgroundException(ex);
                throw;
            }
        }

        public IPeriodicTx TransmitPeriodic(ICanFrame frame, PeriodicTxOptions options)
        {
            ThrowIfDisposed();
            return new ZlgPeriodicTx(this, frame, options);
        }

        public float BusUsage()
        {
            CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.BusUsage);
            var ret = ZLGCAN.ZCAN_GetValue(NativeHandle.DeviceHandle, $"{Options.ChannelIndex}/get_bus_usage/1");
            if (ret != IntPtr.Zero)
            {
                var busUsage = Marshal.PtrToStructure<ZLGCAN.BusUsage>(ret);
                return busUsage.nBusUsage / 100f;
            }
            else
            {
                return 0;
            }
        }

        public CanErrorCounters ErrorCounters()
        {
            //CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.ErrorCounters); always true

            ZLGCAN.ZCAN_ReadChannelErrInfo(_nativeHandle, out var errInfo);
            return new CanErrorCounters()
            {
                TransmitErrorCounter = errInfo.passive_ErrData[1],
                ReceiveErrorCounter = errInfo.passive_ErrData[2]
            };
        }

        public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0)
        {
            ThrowIfDisposed();
            IEnumerable<CanReceiveData> Iterator()
            {
                if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
                using var e = _transceiver.Receive(this, count, timeOut).GetEnumerator();
                while (true)
                {
                    bool moved;
                    try { moved = e.MoveNext(); }
                    catch (Exception ex)
                    {
                        HandleBackgroundException(ex);
                        throw;
                    }
                    if (!moved) yield break;
                    yield return e.Current;
                }
            }
            return Iterator();
        }

        public bool ReadErrorInfo(out ICanErrorInfo? errorInfo)
        {
            errorInfo = null;
            if (ZLGCAN.ZCAN_ReadChannelErrInfo(_nativeHandle, out var errInfo) != ZlgErr.StatusOk ||
                errInfo.error_code == 0)
                return false;
            errorInfo = ZlgErr.ToErrorInfo(errInfo);
            return true;

        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {

                try
                {
                    //StopReceiveLoop();
                    Reset();
                }
                catch (CanKitException ex)
                {
                    CanKitLogger.LogWarning("Failed to reset CAN channel during dispose.", ex);
                }
            }
            finally
            {
                _isDisposed = true;
                try { _owner?.Dispose(); } catch { /*Ignored*/ }
                _owner = null;
            }

        }

        public ZlgBusRtConfigurator Options { get; }

        IBusRTOptionsConfigurator ICanBus.Options => Options;

        public event EventHandler<CanReceiveData> FrameReceived
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

        public event EventHandler<Exception>? BackgroundExceptionOccurred;


        public event EventHandler<ICanErrorInfo> ErrorFrameReceived
        {
            add
            {
                if (!Options.AllowErrorInfo)
                {
                    throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
                }
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
                {
                    throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
                }
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

        public BusState BusState
        {
            get
            {
                ThrowIfDisposed();
                if (ReadErrorInfo(out var info) && info is not null)
                {
                    if ((info.Type & FrameErrorType.BusOff) != 0)
                        return BusState.BusOff;
                    var cs = info.ControllerStatus;
                    if ((cs & (CanControllerStatus.RxPassive | CanControllerStatus.TxPassive)) != 0)
                        return BusState.ErrPassive;
                    if ((cs & (CanControllerStatus.RxWarning | CanControllerStatus.TxWarning)) != 0)
                        return BusState.ErrWarning;
                    return BusState.None;
                }

                return BusState.Unknown;
            }
        }

        public void ApplyConfig(ICanOptions options)
        {
            if (options is not ZlgBusOptions zlgOption)
            {
                throw new CanOptionTypeMismatchException(
                    CanKitErrorCode.ChannelOptionTypeMismatch,
                    typeof(ZlgBusOptions),
                    options.GetType(),
                    $"channel {Options.ChannelIndex}");
            }

            if (zlgOption.ProtocolMode == CanProtocolMode.CanFd)
            {
                ZLGCAN.ZCAN_SetValue(_devicePtr, Options.ChannelIndex + "/canfd_standard", "0");
                var arbitrationRate = zlgOption.BitTiming.Fd?.Nominal.Bitrate
                                  ?? throw new CanBusConfigurationException("Arbitration bitrate must be specified when configuring CAN FD timing.");
                var dataRate = zlgOption.BitTiming.Fd?.Data.Bitrate
                               ?? throw new CanBusConfigurationException("Data bitrate must be specified when configuring CAN FD timing.");

                if (!Enum.IsDefined(typeof(ZlgBaudRate), arbitrationRate) ||
                    !Enum.IsDefined(typeof(ZlgDataDaudRate), dataRate))
                {
                    throw new CanBusConfigurationException(
                        $"Unsupported ZLG CAN FD bitrate setting: abit={arbitrationRate} bps, dbit={dataRate} bps.");
                }

                ZlgErr.ThrowIfError(
                    ZLGCAN.ZCAN_SetValue(
                        _devicePtr,
                        Options.ChannelIndex + "/canfd_abit_baud_rate",
                        arbitrationRate.ToString()),
                    "ZCAN_SetValue(canfd_abit_baud_rate)");

                ZlgErr.ThrowIfError(
                    ZLGCAN.ZCAN_SetValue(
                        _devicePtr,
                        Options.ChannelIndex + "/canfd_dbit_baud_rate",
                        dataRate.ToString()),
                    "ZCAN_SetValue(canfd_dbit_baud_rate)");
            }
            else
            {
                var bitRate = zlgOption.BitTiming.Classic?.Nominal.Bitrate
                              ?? throw new CanBusConfigurationException(
                                  "Bitrate must be specified when configuring classic CAN timing.");

                if (!Enum.IsDefined(typeof(ZlgBaudRate), bitRate))
                {
                    throw new CanBusConfigurationException(
                        $"Unsupported ZLG classic bitrate: {bitRate} bps.");
                }

                if ((Options.Features & CanFeature.CanFd) != 0)
                {
                    ZlgErr.ThrowIfError(
                        ZLGCAN.ZCAN_SetValue(
                            _devicePtr,
                            Options.ChannelIndex + "/canfd_abit_baud_rate", bitRate.ToString()),
                        "ZCAN_SetValue(canfd_abit_baud_rate)");
                }
                else
                {
                    ZlgErr.ThrowIfError(
                        ZLGCAN.ZCAN_SetValue(
                            _devicePtr,
                            Options.ChannelIndex + "/baud_rate", bitRate.ToString()),
                        "ZCAN_SetValue(baud_rate)");
                }
            }


            ZLGCAN.ZCAN_SetValue(
                _devicePtr,
                Options.ChannelIndex + "/work_mode",
                ((int)zlgOption.WorkMode).ToString());

            ZLGCAN.ZCAN_SetValue(
                _devicePtr,
                Options.ChannelIndex + "/initenal_resistance",
                Options.InternalResistance ? "1" : "0");

            if ((Options.Features & CanFeature.TxRetryPolicy) != 0)
            {
                ZlgErr.ThrowIfError(
                    ZLGCAN.ZCAN_SetValue(_devicePtr,
                        Options.ChannelIndex + "/set_tx_retry_policy",
                        Options.TxRetryPolicy == TxRetryPolicy.NoRetry ? "1" : "2"),
                    "ZCAN_SetValue(tx_retry_policy)");
            }

            if ((Options.Features & CanFeature.BusUsage) != 0)
            {
                ZlgErr.ThrowIfError(
                    ZLGCAN.ZCAN_SetValue(_devicePtr,
                        Options.ChannelIndex + "/set_bus_usage_period",
                        Options.BusUsagePeriodTime.ToString()),
                    "ZCAN_SetValue(bus_usage_period)");
                ZlgErr.ThrowIfError(
                    ZLGCAN.ZCAN_SetValue(_devicePtr,
                        Options.ChannelIndex + "/set_bus_usage_enable",
                        Options.BusUsageEnabled ? "1" : "0"),
                    "ZCAN_SetValue(bus_usage_enable)");
            }

            // Cache software filter predicate for polling loop
            _useSoftwareFilter = (Options.EnabledSoftwareFallback & CanFeature.Filters) != 0
                                  && Options.Filter.SoftwareFilterRules.Count > 0;
            _softwareFilterPredicate = _useSoftwareFilter
                ? FilterRule.Build(Options.Filter.SoftwareFilterRules)
                : null;

        }

        public void ApplyConfigAfterInit(ICanOptions options)
        {
            var zlgOption = (ZlgBusOptions)options;
            if (zlgOption.Filter.filterRules.Count > 0)
            {
                if (zlgOption.Filter.filterRules[0] is FilterRule.Mask mask)
                {
                    ZlgErr.ThrowIfError(
                        ZLGCAN.ZCAN_SetValue(
                            _devicePtr,
                            Options.ChannelIndex + "/acc_code", $"0x{mask.AccCode:X}"),
                        "ZCAN_SetValue(acc_code)");
                    ZlgErr.ThrowIfError(
                        ZLGCAN.ZCAN_SetValue(
                            _devicePtr,
                            Options.ChannelIndex + "/acc_mask", $"0x{mask.AccMask:X}"),
                        "ZCAN_SetValue(acc_mask)");

                    foreach (var rule in zlgOption.Filter.filterRules.Skip(1))
                    {
                        if (zlgOption.SoftwareFilterEnabled)
                        {
                            zlgOption.Filter.softwareFilter.Add(rule);
                        }
                    }
                }
                else
                {
                    foreach (var rule in zlgOption.Filter.filterRules)
                    {
                        if (rule is not FilterRule.Range range)
                        {
                            zlgOption.Filter.softwareFilter.Add(rule);
                            continue;
                        }

                        ZlgErr.ThrowIfError(
                            ZLGCAN.ZCAN_SetValue(
                                _devicePtr,
                                Options.ChannelIndex + "/filter_mode",
                                ((int)range.FilterIdType).ToString()),
                            "ZCAN_SetValue(filter_mode)");
                        ZlgErr.ThrowIfError(
                            ZLGCAN.ZCAN_SetValue(
                                _devicePtr,
                                Options.ChannelIndex + "/filter_start", $"0x{range.From:X}"),
                            "ZCAN_SetValue(filter_start)");
                        ZlgErr.ThrowIfError(
                            ZLGCAN.ZCAN_SetValue(
                                _devicePtr,
                                Options.ChannelIndex + "/filter_end", $"0x{range.To:X}"),
                            "ZCAN_SetValue(filter_end)");
                    }

                    ZlgErr.ThrowIfError(
                        ZLGCAN.ZCAN_SetValue(
                            _devicePtr,
                            Options.ChannelIndex + "/filter_ack",
                            "1"),
                        "ZCAN_SetValue(filter_ack)");
                }
                _softwareFilterPredicate = FilterRule.Build(Options.Filter.SoftwareFilterRules);
            }
        }


        public uint GetReceiveCount()
        {
            ThrowIfDisposed();

            var zlgFilterType = Options.ProtocolMode switch
            {
                //CanProtocolMode.Merged => ZlgFrameType.Any,
                CanProtocolMode.CanFd => ZlgFrameType.CanFd,
                _ => ZlgFrameType.CanClassic
            };

            return ZLGCAN.ZCAN_GetReceiveNum(_nativeHandle, (byte)zlgFilterType);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new CanBusDisposedException();
        }

        private void StartReceiveLoopIfNeeded()
        {
            if (_pollTask is { IsCompleted: false }) return;

            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;

            _pollTask = Task.Factory.StartNew(
                () => PollLoop(token),
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );
        }

        private void StopReceiveLoop()
        {
            try
            {
                _pollCts?.Cancel();
                _pollTask?.Wait(500);
            }
            catch { /* ignore on shutdown */ }
            finally
            {
                _pollTask = null;
                _pollCts?.Dispose();
                _pollCts = null;
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
                catch { /*Ignored*/ }
            }, cts.Token);
        }

        private void PollLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (Volatile.Read(ref _subscriberCount) <= 0)
                {
                    break;
                }

                try
                {
                    const uint batch = 256;
                    var count = GetReceiveCount();
                    if (count > 0)
                    {
                        var frames = Receive((int)Math.Min((uint)count, batch));
                        var useSw = (Options.EnabledSoftwareFallback & CanFeature.Filters) != 0
                                    && Options.Filter.SoftwareFilterRules.Count > 0;
                        var pred = useSw ? _softwareFilterPredicate : null;
                        foreach (var frame in frames)
                        {
                            try
                            {
                                if (useSw && pred is not null && !pred(frame.CanFrame))
                                {
                                    continue;
                                }

                                var evSnap = Volatile.Read(ref _frameReceived);
                                evSnap?.Invoke(this, frame);

                                if (Volatile.Read(ref _asyncConsumerCount) > 0)
                                    _asyncRx.Publish(frame);
                            }
                            catch (Exception ex)
                            {
                                CanKitLogger.LogWarning("FrameReceived handler threw an exception.", ex);
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(Options.PollingInterval);
                    }
                    var errSnap = Volatile.Read(ref _errorOccurred);
                    if (errSnap != null && ReadErrorInfo(out var errInfo))
                    {
                        errSnap.Invoke(this, errInfo!);
                    }
                }
                catch (CanBusDisposedException)
                {
                    // 通道或底层已释放，退出
                    break;
                }
                catch (Exception ex)
                {
                    // 非预期异常：记录日志，通知故障并退出循环
                    HandleBackgroundException(ex);
                    break;
                }
            }
        }

        public Task<int> TransmitAsync(IEnumerable<ICanFrame> frames, int timeOut = 0, CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                try { return Transmit(frames, timeOut); }
                catch (Exception ex) { HandleBackgroundException(ex); throw; }
            }, cancellationToken);

        public async Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref _subscriberCount);
            Interlocked.Increment(ref _asyncConsumerCount);
            CheckSubscribers(true);
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            try
            {
                return await _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _ = Interlocked.Decrement(ref _asyncConsumerCount);
                CheckSubscribers(false, true);
            }
        }

#if NET8_0_OR_GREATER
        public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref _asyncConsumerCount);
            CheckSubscribers(true, true);
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
                CheckSubscribers(false);
            }
        }
#endif

        private void CheckSubscribers(bool isIncrease, bool step = false)
        {
            if (step)
            {
                if (isIncrease)
                {
                    if (Interlocked.Increment(ref _subscriberCount) == 1)
                    {
                        StartReceiveLoopIfNeeded();
                    }
                }
                else
                {
                    var now = Interlocked.Decrement(ref _subscriberCount);
                    if (now < 0)
                    {
                        Interlocked.Exchange(ref _subscriberCount, 0);
                        now = 0;
                    }

                    if (now == 0 && Volatile.Read(ref _asyncConsumerCount) == 0)
                        RequestStopReceiveLoop();
                }
            }
            else
            {
                if (isIncrease)
                {
                    if (Volatile.Read(ref _subscriberCount) == 1)
                    {
                        StartReceiveLoopIfNeeded();
                    }
                }
                else
                {
                    if (Volatile.Read(ref _subscriberCount) == 0 && Volatile.Read(ref _asyncConsumerCount) == 0)
                    {
                        RequestStopReceiveLoop();
                    }
                }
            }
        }


        internal int GetAutoSendIndex()
        {
            ThrowIfDisposed();
            int x = 0;
            while (true)
            {
                if (!_autoSendIndexes.Contains(x))
                    break;
                x++;
            }

            _autoSendIndexes.Add(x);
            return x;
        }

        internal bool FreeAutoSendIndex(int index)
        {
            ThrowIfDisposed();
            return _autoSendIndexes.Remove(index);
        }

        private void HandleBackgroundException(Exception ex)
        {
            try { CanKitLogger.LogError("ZLG bus occured background exception.", ex); } catch { }
            try { _asyncRx.ExceptionOccured(ex); } catch { }
            try { var snap = Volatile.Read(ref BackgroundExceptionOccurred); snap?.Invoke(this, ex); } catch { }
        }
    }
}
