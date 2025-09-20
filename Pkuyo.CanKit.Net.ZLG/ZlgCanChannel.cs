using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Diagnostics;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Diagnostics;
using Pkuyo.CanKit.ZLG.Native;
using Pkuyo.CanKit.ZLG.Options;
using Pkuyo.CanKit.ZLG.Transceivers;

namespace Pkuyo.CanKit.ZLG
{
    
    public sealed class ZlgCanChannel : ICanChannel<ZlgChannelRTConfigurator>, INamedCanApplier
    {
        
        internal ZlgCanChannel(ZlgCanDevice device ,IChannelOptions options, ITransceiver transceiver)
        {
            _devicePtr = device.NativeHandler.DangerousGetHandle();
            
            Options = new ZlgChannelRTConfigurator();
            Options.Init((ZlgChannelOptions)options);
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
                var arbitrationRate = options.BitTiming.ArbitrationBitRate
                                      ?? throw new CanChannelConfigurationException("ArbitRation bit rate must be specified when configuring CAN FD timing.");
                var dataRate = options.BitTiming.DataBitRate
                               ?? throw new CanChannelConfigurationException("Data bit rate must be specified when configuring CAN FD timing.");
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
                throw new CanTransceiverMismatchException(typeof(IZlgTransceiver), transceiver?.GetType() ?? typeof(ITransceiver));
            _transceiver = zlg;
            
        }

  
        public void Open()
        {
            ThrowIfDisposed();

            ZlgErr.ThrowIfError(ZLGCAN.ZCAN_StartCAN(_nativeHandle), nameof(ZLGCAN.ZCAN_StartCAN), _nativeHandle);
            _isOpen = true;
        }

        public void Reset()
        {
            ThrowIfDisposed();

            ZlgErr.ThrowIfError(ZLGCAN.ZCAN_ResetCAN(_nativeHandle), nameof(ZLGCAN.ZCAN_ResetCAN), _nativeHandle);

            _isOpen = false;
        }

        public void Close()
        {
            Reset();
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
            var lastTime = DateTime.Now;
            do
            {
                if (isFirst)
                    isFirst = false;
                else
                    Thread.Sleep(Math.Min((DateTime.Now - lastTime).Milliseconds, Options.PollingInterval));
                
                result += _transceiver.Transmit(this, list);
            } while (result < list.Count && DateTime.Now - lastTime <= TimeSpan.FromMilliseconds(timeOut));

            return result;
        }

        public float BusUsage()
        {
            if ((Options.Provider.Features & CanFeature.BusUsage) == 0U)
                throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Provider.Features);
            var ret = ZLGCAN.ZCAN_GetValue(NativeHandle.DeviceHandle, $"{Options.ChannelIndex}/get_bus_usage/1");
            var busUsage = Marshal.PtrToStructure<ZLGCAN.BusUsage>(ret);
            return busUsage.nBusUsage / 10000f;
        }

        public CanErrorCounters ErrorCounters()
        {
            if ((Options.Provider.Features & CanFeature.ErrorCounters) == 0U)
                throw new CanFeatureNotSupportedException(CanFeature.ErrorCounters, Options.Provider.Features);
            
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

        public bool ReadChannelErrorInfo(out ICanErrorInfo? errorInfo)
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
                CanProtocolMode.Merged => ZlgFrameType.Any,
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
                if (_isOpen)
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
            }
            finally
            {
                _isDisposed = true;
            }

        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new CanChannelDisposedException();
        }
        
        public bool ApplyOne<T>(string name, T value)
        {
            return ZLGCAN.ZCAN_SetValue(_devicePtr,
                Options.ChannelIndex + name, value!.ToString()) != 0; //Only ValueType
        }

        public void Apply(ICanOptions options)
        {
            if (options is not ZlgChannelOptions zlgOption)
            {
                throw new CanOptionTypeMismatchException(
                    CanKitErrorCode.ChannelOptionTypeMismatch,
                    typeof(ZlgChannelOptions),
                    options?.GetType() ?? typeof(IChannelOptions),
                    $"channel {Options.ChannelIndex}");
            }

            if (zlgOption.ProtocolMode != CanProtocolMode.Can20)
            {
                var arbitrationRate = zlgOption.BitTiming.ArbitrationBitRate
                                  ?? throw new CanChannelConfigurationException("ArbitRation bit rate must be specified when configuring CAN FD timing.");
                var dataRate = zlgOption.BitTiming.DataBitRate
                               ?? throw new CanChannelConfigurationException("Data bit rate must be specified when configuring CAN FD timing.");
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
                var bitRate = zlgOption.BitTiming.BaudRate
                                      ?? throw new CanChannelConfigurationException("Bit rate must be specified when configuring CAN FD timing.");
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
                }
                else
                {
                    foreach (var rule in zlgOption.Filter.filterRules)
                    {
                        if(rule is not FilterRule.Range range)
                            throw new CanFilterConfigurationException(
                                "ZLG channels only supports the same type of filter rule.");
                        
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
                // 未 Start，睡眠等待
                if (!_isOpen)
                {
                    Thread.Sleep(Options.PollingInterval);
                    continue;
                }
                
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
                        var frames = Receive(Math.Min(count, batch), 0);
                        foreach (var frame in frames)
                        {
                            try
                            {
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

                    if (_errorOccurred != null && ReadChannelErrorInfo(out var errInfo))
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
        
        public bool IsOpen => _isOpen;

        public ZlgChannelHandle NativeHandle => _nativeHandle;

        public ZlgChannelRTConfigurator Options { get; }
        
        IChannelRTOptionsConfigurator ICanChannel.Options => Options;
        
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
        private readonly ZlgChannelHandle _nativeHandle;

        private readonly IntPtr _devicePtr;
        
        private readonly IZlgTransceiver  _transceiver;

        private bool _isDisposed;

        private bool _isOpen;
        
        private readonly object _evtGate = new ();
        
        private EventHandler<CanReceiveData>? _frameReceived; 
        
        private EventHandler<ICanErrorInfo>? _errorOccurred; 
        
        private int _subscriberCount;
        
        private CancellationTokenSource? _pollCts;
        
        private System.Threading.Tasks.Task? _pollTask;
    }
}
