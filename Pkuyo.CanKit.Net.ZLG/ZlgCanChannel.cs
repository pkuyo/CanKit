using System;
using System.Collections.Generic;
using System.Linq;
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
            Options = new ZlgChannelRTConfigurator();
            Options.Init((ZlgChannelOptions)options);
            _devicePtr = device.NativeHandler.DangerousGetHandle();
            options.Apply(this, true);
            
            ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG config = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG
            {
                can_type = options.ProtocolMode == CanProtocolMode.Can20 ? 0U : 1U
            };
            config.config.can.acc_code = 0;
            config.config.can.acc_mask = 0x1FFFFFFF;
            var handle = ZLGCAN.ZCAN_InitCAN(device.NativeHandler, (uint)Options.ChannelIndex, ref config);
            handle.SetDevice(device.NativeHandler.DangerousGetHandle());
            if (handle.IsInvalid)
                throw new Exception(); //TODO:异常处理
            
   
            _nativeHandle = handle;
            
            if (transceiver is not IZlgTransceiver zlg)
                throw new Exception(); //TODO:异常处理
            
            _transceiver = zlg;
        }

  
        public void Start()
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

        public void Stop()
        {
            throw new NotImplementedException();
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
        public IEnumerable<CanReceiveData> ReceiveAll(CanFrameType filterType)
        {
            ThrowIfDisposed();

            var count = CanReceiveCount(filterType);

            if (count == 0)
                return [];
      
            return _transceiver.Receive(this, count);
        }
        public IEnumerable<CanReceiveData> Receive(CanFrameType filterType, uint count = 1, int timeOut = -1)
        {
            ThrowIfDisposed();
            return _transceiver.Receive(this, count, timeOut);
        }
        
        

        public uint CanReceiveCount(CanFrameType filterType)
        {
            ThrowIfDisposed();

            var zlgFilterType = filterType switch
            {
                CanFrameType.Any => ZlgFrameType.Any,
                CanFrameType.CanFd => ZlgFrameType.CanFd,
                CanFrameType.CanClassic => ZlgFrameType.CanClassic,
                _ => throw new NotSupportedException("ZlgCan ReceiveCount 仅支持 Can/CanFD/Any")
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

        public bool IsOpen => _isOpen;

        public ZlgChannelHandle NativeHandle => _nativeHandle;

        public ZlgChannelRTConfigurator Options { get; }
        
        IChannelRTOptionsConfigurator ICanChannel.Options => Options;
        

        private ZlgChannelHandle _nativeHandle;

        private IntPtr _devicePtr;
        
        private readonly IZlgTransceiver  _transceiver;

        private bool _isDisposed;

        private bool _isOpen;
        public bool ApplyOne<T>(string name, T value)
        {
            /*
            if (name[0] == '/')
            {
                return ZLGCAN.ZCAN_SetValue(_devicePtr,Options.ChannelIndex.ToString() +
                                                      name[0], value.ToString()) != 0;
            }
            */
            if (value is BitTiming bitTiming)
            {
                var result = 1U;
                if (bitTiming.ArbitrationBitRate != null)
                {
                 
                    result &= ZLGCAN.ZCAN_SetValue(_devicePtr,Options.ChannelIndex +
                                                              "/canfd_abit_baud_rate", 
                        bitTiming.ArbitrationBitRate.ToString());
                    result &= ZLGCAN.ZCAN_SetValue(_devicePtr,Options.ChannelIndex +
                                                               "/canfd_dbit_baud_rate", 
                        bitTiming.DataBitRate.ToString());
                }
                else if (bitTiming.BaudRate != null)
                {
                    result &= ZLGCAN.ZCAN_SetValue(_devicePtr,Options.ChannelIndex +
                                                                "/baud_rate", bitTiming.BaudRate.ToString());
                }

                return result != 0;
            }
            return false;
        }

        public CanOptionType ApplierStatus => _nativeHandle is { IsInvalid: false, IsClosed: false } ?
            CanOptionType.Runtime : 
            CanOptionType.Init;
    }
}
