using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Native;
using Pkuyo.CanKit.ZLG.Options;
using Pkuyo.CanKit.ZLG.Transceivers;

namespace Pkuyo.CanKit.ZLG
{
    public sealed class ZlgCanChannel : ICanChannel<ZlgChannelRTConfigurator>, ICanApplier
    {
        
        internal ZlgCanChannel(ZlgCanDevice device ,IChannelOptions options, ITransceiver transceiver)
        {
            _devicePtr = device.NativeHandler.DangerousGetHandle();
            
            Options = new ZlgChannelRTConfigurator();
            Options.Init((ZlgChannelOptions)options);
            options.Apply(this, true);
            
            ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG config = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG
            {
                can_type = options.ProtocolMode == CanProtocolMode.Can20 ? 0U : 1U,
            };
            if (options.ProtocolMode == CanProtocolMode.Can20)
            {
                config.config.can.mode = (byte)options.WorkMode;
                if (options.Filter != null && 
                    options.Filter.filterRules.Count > 0 && 
                    options.Filter.filterRules[0] is FilterRule.Mask mask)
                {
                    config.config.can.acc_code = mask.AccCode;
                    config.config.can.acc_mask = mask.AccMask;
                    config.config.can.filter = (byte)Options.MaskFilterType;
                    //TODO:多与一项时的警告
                }
                else
                {
                    config.config.can.acc_code = 0;
                    config.config.can.acc_mask = 0xffffffff;
                }
            }
            else
            {
                
            }

            var handle = ZLGCAN.ZCAN_InitCAN(device.NativeHandler, (uint)Options.ChannelIndex, ref config);
            handle.SetDevice(device.NativeHandler.DangerousGetHandle());
            
            if (handle.IsInvalid)
                throw new Exception(); //TODO:异常处理
            _nativeHandle = handle;
            
            if (transceiver is not IZlgTransceiver zlg)
                throw new Exception(); //TODO:异常处理
            _transceiver = zlg;
            
        }

  
        public void Open()
        {
            ThrowIfDisposed();

            //TODO: 异常处理
            if (ZLGCAN.ZCAN_StartCAN(_nativeHandle) == 0)
                throw new Exception();
            _isOpen = true;
        }

        public void Reset()
        {
            ThrowIfDisposed();

            //TODO: 异常处理
            ZLGCAN.ZCAN_ResetCAN(_nativeHandle);

            _isOpen = false;
        }

        public void Close()
        {
            Reset();
        }

        public void CleanBuffer()
        {
            ThrowIfDisposed();

            //TODO: 异常处理
            ZLGCAN.ZCAN_ClearBuffer(_nativeHandle);
        }

        public uint Transmit(params CanTransmitData[] frames)
        {
            ThrowIfDisposed();
            return _transceiver.Transmit(this, frames);
        }
        public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = -1)
        {
            ThrowIfDisposed();
            return _transceiver.Receive(this, count, timeOut);
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
            try
            {
                ThrowIfDisposed();
                Reset();
            }
            finally
            {
                _isDisposed = true;
            }
      
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new InvalidOperationException();
        }
        
        public bool ApplyOne<T>(string name, T value)
        {
            return ZLGCAN.ZCAN_SetValue(_devicePtr,
                Options.ChannelIndex.ToString() + name[0], value.ToString()) != 0;
        }

        public void Apply(ICanOptions options)
        {
            var zlgOption = (ZlgChannelOptions)options;
            
            //TODO:异常处理
            
            //BitTiming
            if (zlgOption.BitTiming.ArbitrationBitRate != null)
            {

                ZLGCAN.ZCAN_SetValue(_devicePtr, 
                    Options.ChannelIndex + "/canfd_abit_baud_rate",
                    zlgOption.BitTiming.ArbitrationBitRate.ToString());
                ZLGCAN.ZCAN_SetValue(_devicePtr, 
                    Options.ChannelIndex + "/canfd_dbit_baud_rate",
                    zlgOption.BitTiming.DataBitRate.ToString());
            }
            else if (zlgOption.BitTiming.BaudRate != null)
            {
                ZLGCAN.ZCAN_SetValue(_devicePtr, 
                    Options.ChannelIndex + "/baud_rate", zlgOption.BitTiming.BaudRate.ToString());
            }
            
            
            //WorkMode
            ZLGCAN.ZCAN_SetValue(_devicePtr, 
                Options.ChannelIndex + "/work_mode", ((int)Options.WorkMode).ToString());

            //Filter
            if (zlgOption.Filter != null && zlgOption.Filter.filterRules.Count > 0)
            {
                if (zlgOption.Filter.filterRules[0] is FilterRule.Mask mask)
                {
                    if (zlgOption.Filter.filterRules.Count > 1)
                        throw new NotSupportedException(); //TODO:异常处理
                    
                    ZLGCAN.ZCAN_SetValue(_devicePtr, 
                        Options.ChannelIndex + "/acc_code", mask.AccCode.ToString());
                    ZLGCAN.ZCAN_SetValue(_devicePtr, 
                        Options.ChannelIndex + "/acc_mask", mask.AccMask.ToString());
                    
                }
                else
                {
                    foreach (var range in zlgOption.Filter.filterRules.OfType<FilterRule.Range>())
                    {
                        ZLGCAN.ZCAN_SetValue(_devicePtr, 
                            Options.ChannelIndex + "/filter_mode",((int)range.IdIdType).ToString());
                        ZLGCAN.ZCAN_SetValue(_devicePtr, 
                            Options.ChannelIndex + "/filter_start",range.From.ToString());
                        ZLGCAN.ZCAN_SetValue(_devicePtr, 
                            Options.ChannelIndex + "/filter_end",range.To.ToString());
                    }

                    ZLGCAN.ZCAN_SetValue(_devicePtr,
                        Options.ChannelIndex + "/filter_ack", "1");
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
                    Thread.Sleep(20);
                    continue;
                }

                // 防御性：无订阅者时退出（正常由 StopPolling 触发，双保险）
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
                                // TODO: 异常处理
                                System.Diagnostics.Debug.WriteLine($"FrameReceived handler error: {ex}");
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(20);
                    }

                    if (_errorOccurred != null)
                    {
                        var errInfo = new ZLGCAN.ZCAN_CHANNEL_ERROR_INFO();
                        ZLGCAN.ZCAN_ReadChannelErrInfo(_nativeHandle, ref errInfo);
                        _errorOccurred?.Invoke(this,new CanErrorFrame());
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
                    //TODO: 异常处理
                    System.Diagnostics.Debug.WriteLine($"PollLoop error: {ex}");
                    Thread.Sleep(20);
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

        public event EventHandler<CanErrorFrame> ErrorOccurred
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
        
        private EventHandler<CanReceiveData> _frameReceived; 
        
        private EventHandler<CanErrorFrame> _errorOccurred; 
        
        private int _subscriberCount;
        
        private CancellationTokenSource _pollCts;
        
        private System.Threading.Tasks.Task _pollTask;
    }
}
