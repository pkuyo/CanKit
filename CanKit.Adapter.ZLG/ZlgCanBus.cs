using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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

namespace CanKit.Adapter.ZLG
{

    public sealed class ZlgCanBus : ICanBus<ZlgBusRtConfigurator>, INamedCanApplier, IBusOwnership
    {

        internal ZlgCanBus(ZlgCanDevice device, IBusOptions options, ITransceiver transceiver)
        {
            _devicePtr = device.NativeHandler.DangerousGetHandle();

            Options = new ZlgBusRtConfigurator();
            Options.Init((ZlgBusOptions)options);
            options.Apply(this, true);

            var provider = Options.Provider as ZlgCanProvider;

            ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG config = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG
            {
                can_type = options.ProtocolMode == CanProtocolMode.Can20 ? 0U : 1U,
            };
            if (options.ProtocolMode == CanProtocolMode.Can20)
            {
                config.config.can.mode = (byte)options.WorkMode;


            }
            else
            {
                var arbitrationRate = options.BitTiming.Fd?.Nominal.Bitrate
                                      ?? throw new CanChannelConfigurationException("Arbitration bitrate must be specified when configuring CAN FD timing.");
                var dataRate = options.BitTiming.Fd?.Data.Bitrate
                               ?? throw new CanChannelConfigurationException("Data bitrate must be specified when configuring CAN FD timing.");
                config.config.canfd.abit_timing = arbitrationRate;
                config.config.canfd.dbit_timing = dataRate;
            }

            if (options.Filter.filterRules.Count > 0)
            {
                if (options.Filter.filterRules[0] is FilterRule.Mask mask)
                {
                    if (provider is not null)
                        ZlgErr.ThrowIfNotSupport(provider.ZlgFeature, ZlgFeature.MaskFilter);

                    config.config.can.acc_code = mask.AccCode;
                    config.config.can.acc_mask = mask.AccMask;
                    config.config.can.filter = (byte)Options.MaskFilterType;

                }
                else
                {
                    config.config.can.acc_code = 0;
                    config.config.can.acc_mask = 0xffffffff;
                }

            }

            var handle = ZLGCAN.ZCAN_InitCAN(device.NativeHandler, (uint)Options.ChannelIndex, ref config);
            handle.SetDevice(device.NativeHandler.DangerousGetHandle());

            ZlgErr.ThrowIfInvalid(handle, nameof(ZLGCAN.ZCAN_InitCAN));
            _nativeHandle = handle;

            Reset();

            if (transceiver is not IZlgTransceiver zlg)
                throw new CanTransceiverMismatchException(typeof(IZlgTransceiver), transceiver.GetType());
            _transceiver = zlg;

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

        public uint Transmit(IEnumerable<CanTransmitData> frames, int timeOut = 0)
        {
            ThrowIfDisposed();

            var list = frames.ToList();
            bool isFirst = true;
            uint result = 0;
            var startTime = Environment.TickCount;
            do
            {
                if (isFirst)
                    isFirst = false;
                else
                    Thread.Sleep(Math.Min(Environment.TickCount - startTime, Options.PollingInterval));

                result += _transceiver.Transmit(this, list);
            } while (result < list.Count && Environment.TickCount - startTime <= timeOut);

            return result;
        }

        public IPeriodicTx TransmitPeriodic(CanTransmitData frame, PeriodicTxOptions options)
        {
            ThrowIfDisposed();
            //TODO:定时发送
            throw new NotImplementedException();
        }

        public float BusUsage()
        {
            if ((Options.Features & CanFeature.BusUsage) == 0U)
                throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Features);
            var ret = ZLGCAN.ZCAN_GetValue(NativeHandle.DeviceHandle, $"{Options.ChannelIndex}/get_bus_usage/1");
            var busUsage = Marshal.PtrToStructure<ZLGCAN.BusUsage>(ret);
            return busUsage.nBusUsage / 10000f;
        }

        public CanErrorCounters ErrorCounters()
        {
            if ((Options.Features & CanFeature.ErrorCounters) == 0U)
                throw new CanFeatureNotSupportedException(CanFeature.ErrorCounters, Options.Features);

            var errInfo = new ZLGCAN.ZCAN_CHANNEL_ERROR_INFO();
            ZLGCAN.ZCAN_ReadChannelErrInfo(_nativeHandle, ref errInfo);
            return new CanErrorCounters()
            {
                TransmitErrorCounter = errInfo.passive_ErrData[1],
                ReceiveErrorCounter = errInfo.passive_ErrData[2]
            };
        }

        public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = 0)
        {
            ThrowIfDisposed();
            return _transceiver.Receive(this, count, timeOut);
        }

        public bool ReadErrorInfo(out ICanErrorInfo? errorInfo)
        {
            errorInfo = null;

            var errInfo = new ZLGCAN.ZCAN_CHANNEL_ERROR_INFO();
            if (ZLGCAN.ZCAN_ReadChannelErrInfo(_nativeHandle, ref errInfo) != ZlgErr.StatusOk ||
                errInfo.error_code == 0)
                return false;

            errorInfo = ZlgErr.ToErrorInfo(errInfo);
            return true;

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
                try { _owner?.Dispose(); } catch { }
                _owner = null;
            }

        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new CanBusDisposedException();
        }

        public bool ApplyOne<T>(object id, T value)
        {
            return ZLGCAN.ZCAN_SetValue(_devicePtr,
                Options.ChannelIndex + (string)id, value!.ToString()) != 0; //Only ValueType
        }

        public void Apply(ICanOptions options)
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
                var arbitrationRate = zlgOption.BitTiming.Fd?.Nominal.Bitrate
                                  ?? throw new CanChannelConfigurationException("Arbitration bitrate must be specified when configuring CAN FD timing.");
                var dataRate = zlgOption.BitTiming.Fd?.Data.Bitrate
                               ?? throw new CanChannelConfigurationException("Data bitrate must be specified when configuring CAN FD timing.");

                if (!Enum.IsDefined(typeof(ZlgBaudRate), arbitrationRate) ||
                    !Enum.IsDefined(typeof(ZlgDataDaudRate), dataRate))
                {
                    //TODO:异常处理，不支持的波特率设置
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
                                      ?? throw new CanChannelConfigurationException("Bitrate must be specified when configuring classic CAN timing.");

                if (!Enum.IsDefined(typeof(ZlgBaudRate), bitRate))
                {
                    //TODO:异常处理，不支持的波特率设置
                }
                ZlgErr.ThrowIfError(
                    ZLGCAN.ZCAN_SetValue(
                        _devicePtr,
                        Options.ChannelIndex + "/baud_rate", bitRate.ToString()),
                    "ZCAN_SetValue(baud_rate)");
            }

            ZlgErr.ThrowIfError(
                ZLGCAN.ZCAN_SetValue(
                    _devicePtr,
                    Options.ChannelIndex + "/work_mode",
                    ((int)zlgOption.WorkMode).ToString()),
                "ZCAN_SetValue(work_mode)");

            if (zlgOption.Filter.filterRules.Count > 0)
            {
                if (zlgOption.Filter.filterRules[0] is FilterRule.Mask mask)
                {
                    ZlgErr.ThrowIfError(
                        ZLGCAN.ZCAN_SetValue(
                            _devicePtr,
                            Options.ChannelIndex + "/acc_code",
                            mask.AccCode.ToString()),
                        "ZCAN_SetValue(acc_code)");
                    ZlgErr.ThrowIfError(
                        ZLGCAN.ZCAN_SetValue(
                            _devicePtr,
                            Options.ChannelIndex + "/acc_mask",
                            mask.AccMask.ToString()),
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
                                Options.ChannelIndex + "/filter_start",
                                range.From.ToString()),
                            "ZCAN_SetValue(filter_start)");
                        ZlgErr.ThrowIfError(
                            ZLGCAN.ZCAN_SetValue(
                                _devicePtr,
                                Options.ChannelIndex + "/filter_end",
                                range.To.ToString()),
                            "ZCAN_SetValue(filter_end)");
                    }

                    ZlgErr.ThrowIfError(
                        ZLGCAN.ZCAN_SetValue(
                            _devicePtr,
                            Options.ChannelIndex + "/filter_ack",
                            "1"),
                        "ZCAN_SetValue(filter_ack)");
                }
            }
        }

        private void StartPollingIfNeeded()
        {
            if (_pollTask is { IsCompleted: false }) return;

            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;

            _pollTask = System.Threading.Tasks.Task.Factory.StartNew(
                () => PollLoop(token),
                token,
                System.Threading.Tasks.TaskCreationOptions.LongRunning,
                System.Threading.Tasks.TaskScheduler.Default
            );
        }

        private void StopPolling()
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
                        var frames = Receive(Math.Min(count, batch));
                        var useSw = (Options.EnabledSoftwareFallbackE & CanFeature.Filters) != 0
                                    && Options.Filter.SoftwareFilterRules.Count > 0;
                        var pred = useSw ? FilterRule.Build(Options.Filter.SoftwareFilterRules) : null;
                        foreach (var frame in frames)
                        {
                            try
                            {
                                if (useSw && pred is not null && !pred(frame.CanFrame))
                                {
                                    continue;
                                }
                                _frameReceived?.Invoke(this, frame);
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

                    if (_errorOccurred != null && ReadErrorInfo(out var errInfo))
                    {
                        _errorOccurred.Invoke(this, errInfo!);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // 通道或底层已释放，退出
                    break;
                }
                catch (Exception ex)
                {
                    // 其他异常：短暂休眠并继续，避免热循环
                    CanKitLogger.LogWarning("Error occurred while polling ZLG CAN channel.", ex);
                    Thread.Sleep(Options.PollingInterval);
                }
            }
        }

        private void CheckSubscribers(bool isIncrease)
        {
            if (isIncrease)
            {
                if (_subscriberCount == 1)
                {
                    StartPollingIfNeeded();
                }
            }
            else
            {
                if (_subscriberCount == 0)
                {
                    StopPolling();
                }
            }
        }

        public ZlgChannelHandle NativeHandle => _nativeHandle;

        public ZlgBusRtConfigurator Options { get; }

        IBusRTOptionsConfigurator ICanBus.Options => Options;

        public CanOptionType ApplierStatus => _nativeHandle is { IsInvalid: false, IsClosed: false } ?
            CanOptionType.Runtime :
            CanOptionType.Init;

        public event EventHandler<CanReceiveData> FrameReceived
        {
            add
            {
                lock (_evtGate)
                {
                    _frameReceived += value;
                    _subscriberCount++;
                    CheckSubscribers(true);
                }
            }
            remove
            {
                lock (_evtGate)
                {
                    _frameReceived -= value;
                    _subscriberCount = Math.Max(0, _subscriberCount - 1);
                    CheckSubscribers(false);
                }
            }
        }

        public event EventHandler<ICanErrorInfo> ErrorOccurred
        {
            add
            {
                lock (_evtGate)
                {
                    _errorOccurred += value;
                    _subscriberCount++;
                    CheckSubscribers(true);
                }

            }
            remove
            {
                lock (_evtGate)
                {
                    _errorOccurred -= value;
                    _subscriberCount--;
                    CheckSubscribers(false);
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
                    if ((info.Kind & FrameErrorKind.BusOff) != 0)
                        return BusState.BusOff;
                    if ((info.Kind & FrameErrorKind.Passive) != 0)
                        return BusState.ErrPassive;
                    if ((info.Kind & FrameErrorKind.Warning) != 0)
                        return BusState.ErrWarning;
                    return BusState.None;
                }

                return BusState.Unknown;
            }
        }
        private readonly ZlgChannelHandle _nativeHandle;

        private readonly IntPtr _devicePtr;

        private readonly IZlgTransceiver _transceiver;

        private bool _isDisposed;

        private readonly object _evtGate = new();

        private EventHandler<CanReceiveData>? _frameReceived;

        private EventHandler<ICanErrorInfo>? _errorOccurred;

        private int _subscriberCount;

        private CancellationTokenSource? _pollCts;

        private System.Threading.Tasks.Task? _pollTask;

        private IDisposable? _owner;

        public void AttachOwner(IDisposable owner)
        {
            _owner = owner;
        }
    }
}
