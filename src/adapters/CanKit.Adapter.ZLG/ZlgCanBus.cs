using System;
using System.Collections.Generic;
using System.Linq;
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
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Diagnostics;
using CanKit.Adapter.ZLG.Native;
using CanKit.Adapter.ZLG.Options;
using CanKit.Adapter.ZLG.Transceivers;
using CanKit.Adapter.ZLG.Utils;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;

namespace CanKit.Adapter.ZLG
{

    public sealed class ZlgCanBus : ICanBus<ZlgBusRtConfigurator>, IOwnership
    {
        private readonly HashSet<int> _autoSendIndexes = new();
        private readonly object _autoSendGate = new();

        private readonly IntPtr _devicePtr;

        private readonly object _evtGate = new();

        private readonly ZlgChannelHandle _handle;

        private readonly ITransceiver _transceiver;

        private EventHandler<ICanErrorInfo>? _errorOccurred;

        private EventHandler<CanReceiveData>? _frameReceived;

        private bool _isDisposed;

        private IDisposable? _owner;

        private CancellationTokenSource? _pollCts;

        private Task? _pollTask;
        private Func<CanFrame, bool>? _softwareFilterPredicate;
        private bool _useSoftwareFilter;
        private readonly AsyncFramePipe<CanReceiveData> _asyncRx;
        private readonly ZlgDeviceKind _deviceType;
        internal ZlgCanBus(ZlgCanDevice device, IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
        {
            _devicePtr = device.NativeHandler.DangerousGetHandle();
            _deviceType = (ZlgDeviceKind)((ZlgDeviceType)device.Options.DeviceType).Code;

            Options = new ZlgBusRtConfigurator();
            Options.Init((ZlgBusOptions)options);
            _asyncRx = new AsyncFramePipe<CanReceiveData>(Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null);
            CanKitLogger.LogInformation($"ZLG: Initializing channel '{Options.ChannelName?? Options.ChannelIndex.ToString()}', Mode={Options.ProtocolMode}...");
            ApplyConfig(options);

            ZLGCAN.ZCAN_SetValue(_devicePtr, options.ChannelIndex+"/clear_auto_send", "0");

            ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG config = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG
            {
                can_type = options.ProtocolMode == CanProtocolMode.Can20 ? 0U : 1U,
            };
            if (device.Options.DeviceType == ZlgDeviceType.ZCAN_USBCANFD_100U ||
                device.Options.DeviceType == ZlgDeviceType.ZCAN_USBCANFD_200U ||
                device.Options.DeviceType == ZlgDeviceType.ZCAN_USBCANFD_400U ||
                device.Options.DeviceType == ZlgDeviceType.ZCAN_USBCANFD_800U ||
                device.Options.DeviceType == ZlgDeviceType.ZCAN_USBCANFD_MINI)
            {
                config.can_type = 1U;
            }
            config.config.can.mode = (byte)options.WorkMode;
            CanKitLogger.LogInformation($"ZLG: Initializing on '{options.ChannelIndex}', Mode={options.ProtocolMode}, Features={Options.Features}");
            config.config.can.acc_code = 0;
            config.config.can.acc_mask = 0xffffffff;
            if (options.Filter.FilterRules.Count > 0)
            {
                if (options.Filter.FilterRules[0] is FilterRule.Mask mask)
                {
                    config.config.can.acc_code = mask.AccCode;
                    config.config.can.acc_mask = mask.AccMask;
                    config.config.can.filter = (byte)mask.FilterIdType;

                }
            }


            var handle = ZLGCAN.ZCAN_InitCAN(device.NativeHandler, (uint)Options.ChannelIndex, ref config);
            CanKitLogger.LogInformation("ZLG: Initialize succeeded.");
            handle.SetDevice(device.NativeHandler.DangerousGetHandle());
            _handle = handle;
            NativeHandle = new BusNativeHandle(_handle.DangerousGetHandle());
            ZlgErr.ThrowIfInvalid(handle, nameof(ZLGCAN.ZCAN_InitCAN));
            Reset();
            ApplyConfigAfterInit(options);

            ZlgErr.ThrowIfError(ZLGCAN.ZCAN_StartCAN(_handle), nameof(ZLGCAN.ZCAN_StartCAN), _handle);
            _transceiver = transceiver;
            CanKitLogger.LogDebug("ZLG: Initial options applied.");
            StartReceiveLoop();
        }

        public ZlgChannelHandle Handle => _handle;

        public void AttachOwner(IDisposable owner)
        {
            _owner = owner;
        }

        public BusNativeHandle NativeHandle { get; }

        public void Reset()
        {
            ThrowIfDisposed();
            ZlgErr.ThrowIfError(ZLGCAN.ZCAN_ResetCAN(_handle), nameof(ZLGCAN.ZCAN_ResetCAN), _handle);
        }


        public void ClearBuffer()
        {
            ThrowIfDisposed();
            ZlgErr.ThrowIfError(ZLGCAN.ZCAN_ClearBuffer(_handle), nameof(ZLGCAN.ZCAN_ClearBuffer), _handle);
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
            if ((Options.Features & CanFeature.CyclicTx) != 0)
            {
                if (GetAutoSendIndex(false) < MaxFilterCount)
                {
                    return new ZlgPeriodicTx(this, frame, options);
                }
                if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) == 0)
                {
                    throw new CanBusException(CanKitErrorCode.FeatureNotSupported,
                        $"ZlgCan Bus only supported {MaxFilterCount} set of filters.");
                }
            }
            if ((Options.EnabledSoftwareFallback & CanFeature.CyclicTx) != 0)
                return SoftwarePeriodicTx.Start(this, frame, options);
            throw new CanFeatureNotSupportedException(CanFeature.CyclicTx, Options.Features);
        }

        public float BusUsage()
        {
            CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.BusUsage);
            var ret = ZLGCAN.ZCAN_GetValue(Handle.DeviceHandle, $"{Options.ChannelIndex}/get_bus_usage/1");
            if (ret != IntPtr.Zero)
            {
                var busUsage = Marshal.PtrToStructure<ZLGCAN.BusUsage>(ret);
                return busUsage.nBusUsage/ 100f;
            }
            else
            {
                return 0;
            }
        }

        public CanErrorCounters ErrorCounters()
        {
            //CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.ErrorCounters); always true

            ZLGCAN.ZCAN_ReadChannelErrInfo(_handle, out var errInfo);
            return new CanErrorCounters()
            {
                TransmitErrorCounter = errInfo.passive_ErrData[1],
                ReceiveErrorCounter = errInfo.passive_ErrData[2]
            };
        }

        public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0)
        {
            ThrowIfDisposed();

            // To prevent cross-handler contention when subscribing to FrameReceived or ErrorFrameReceived, handle all messages asynchronously.
            return ReceiveAsync(count, timeOut).GetAwaiter().GetResult();

        }

        public bool ReadErrorInfo(out ICanErrorInfo? errorInfo)
        {
            errorInfo = null;
            if (ZLGCAN.ZCAN_ReadChannelErrInfo(_handle, out var errInfo) != ZlgErr.StatusOk ||
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
                _isDisposed = true;
                try
                {
                    StopReceiveLoop();
                    Reset();
                }
                catch (CanKitException ex)
                {
                    CanKitLogger.LogWarning("Failed to reset CAN channel during dispose.", ex);
                }
            }
            finally
            {
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

        public event EventHandler<Exception>? BackgroundExceptionOccurred;
        public event EventHandler<Exception>? FaultOccurred;


        public event EventHandler<ICanErrorInfo> ErrorFrameReceived
        {
            add
            {
                if (!Options.AllowErrorInfo)
                {
                    throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
                }
                lock (_evtGate)
                {
                    _errorOccurred += value;
                }
            }
            remove
            {
                if (!Options.AllowErrorInfo)
                {
                    throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
                }
                lock (_evtGate)
                {
                    _errorOccurred -= value;
                }
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

            if (zlgOption.MergeReceive.HasValue && zlgOption.ZlgFeatures.HasFlag(ZlgFeature.MergeReceive))
            {
                ZlgErr.ThrowIfError(
                    ZLGCAN.ZCAN_SetValue(
                        _devicePtr,
                        Options.ChannelIndex + "/set_device_recv_merge",
                        zlgOption.MergeReceive.Value ? "1" : "0"),
                    "ZCAN_SetValue(set_device_recv_merge)");
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

        }
        public void ApplyConfigAfterInit(ICanOptions options)
        {
            var zlgOption = (ZlgBusOptions)options;
            ZLGCAN.ZCAN_SetValue(
                _devicePtr,
                Options.ChannelIndex + "/initenal_resistance",
                Options.InternalResistance ? "1" : "0");
            if (zlgOption.Filter.FilterRules.Count > 0)
            {
                if (zlgOption.Filter.FilterRules[0] is FilterRule.Mask mask
#if !FAKE
                    && (options.Features & CanFeature.MaskFilter) != 0
#endif
                    )
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

                    foreach (var rule in zlgOption.Filter.FilterRules.Skip(1))
                    {
                        if (zlgOption.EnabledSoftwareFallback.HasFlag(CanFeature.RangeFilter))
                        {
                            zlgOption.Filter.SoftwareFilterRules.Add(rule);
                        }
                    }
                }
                else
                {
                    foreach (var rule in zlgOption.Filter.FilterRules)
                    {
                        if (rule is not FilterRule.Range range || !zlgOption.Features.HasFlag(CanFeature.RangeFilter))
                        {
                            zlgOption.Filter.SoftwareFilterRules.Add(rule);
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

                    if (zlgOption.Features.HasFlag(CanFeature.RangeFilter))
                    {
                        ZlgErr.ThrowIfError(
                            ZLGCAN.ZCAN_SetValue(
                                _devicePtr,
                                Options.ChannelIndex + "/filter_ack",
                                "1"),
                            "ZCAN_SetValue(filter_ack)");
                    }
                }
                // Cache software filter predicate for polling loop
                _useSoftwareFilter = (Options.EnabledSoftwareFallback & CanFeature.Filters) != 0
                                     && Options.Filter.SoftwareFilterRules.Count > 0;
                _softwareFilterPredicate = _useSoftwareFilter
                    ? FilterRule.Build(Options.Filter.SoftwareFilterRules)
                    : null;
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

        }


        public int GetReceiveCount()
        {
            ThrowIfDisposed();

            return (int)ZLGCAN.ZCAN_GetReceiveNum(_handle, (byte)((byte)(Options.ProtocolMode)-1));
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new CanBusDisposedException();
        }

        private void StartReceiveLoop()
        {

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
                CanKitLogger.LogDebug("ZLG: Poll loop stopped.");
            }
        }

        private void PollLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    const int batch = 64;
                    var count = GetReceiveCount();
                    if (count > 0)
                    {
                        var frames = _transceiver.Receive(this, Math.Min(count, batch));
                        var pred = _useSoftwareFilter ? _softwareFilterPredicate : null;
                        foreach (var frame in frames)
                        {
                            try
                            {
                                if (_useSoftwareFilter && pred is not null && !pred(frame.CanFrame))
                                {
                                    continue;
                                }

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
                            }
                            catch (Exception ex)
                            {
                                CanKitLogger.LogWarning("FrameReceived handler threw an exception.", ex);
                            }
                        }
                    }
                    else
                    {
                        PreciseDelay.Delay(TimeSpan.FromMilliseconds(Math.Max(1, Options.PollingInterval)), ct: token);
                    }
                    var errSnap = Volatile.Read(ref _errorOccurred);
                    if (errSnap != null && ReadErrorInfo(out var errInfo))
                    {
                        try
                        {
                            errSnap.Invoke(this, errInfo!);
                        }
                        catch (Exception e)
                        {
                            HandleBackgroundException(e, false);
                        }
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
                    HandleBackgroundException(ex, true);
                    break;
                }
            }
        }

        public Task<int> TransmitAsync(IEnumerable<CanFrame> frames, int timeOut = 0,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Transmit(frames, timeOut));

        public Task<int> TransmitAsync(CanFrame frame, CancellationToken cancellationToken = default)
            => Task.FromResult(Transmit(frame));

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
            {
                yield return item;
            }

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

        private void HandleBackgroundException(Exception ex, bool fault)
        {
            try { CanKitLogger.LogError("ZLG bus occured background exception.", ex); } catch { }

            if (fault)
            {
                try { _asyncRx.ExceptionOccured(ex); } catch { }
                try
                {
                    var faultSpan = Volatile.Read(ref FaultOccurred);
                    faultSpan?.Invoke(this, ex);
                }
                catch { }
                _pollCts?.Cancel();
            }

            try { var snap = Volatile.Read(ref BackgroundExceptionOccurred); snap?.Invoke(this, ex); } catch { }
        }

        private int MaxFilterCount
        {
            get => _deviceType switch
            {
                ZlgDeviceKind.ZCAN_USBCANFD_100U or ZlgDeviceKind.ZCAN_USBCANFD_200U
                    or ZlgDeviceKind.ZCAN_USBCANFD_400U => 100,
                ZlgDeviceKind.ZCAN_USBCANFD_800U => 32,
                ZlgDeviceKind.ZCAN_USBCAN_2E_U or ZlgDeviceKind.ZCAN_USBCAN_4E_U
                    or ZlgDeviceKind.ZCAN_USBCAN_8E_U => 32,
                _ => int.MaxValue, //Unknown
            };
        }
    }
}
