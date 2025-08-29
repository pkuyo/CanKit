using System;
using System.Collections.Generic;
using System.Linq;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Definitions;
using ZlgCAN.Net.Core.Transceivers;
using ZlgCAN.Net.Native;

namespace ZlgCAN.Net.Core.Impl
{
    public sealed class ZlgCanChannel : ICanChannel
    {

        internal ZlgCanChannel(IntPtr nativePtr, IChannelOptions options, IEnumerable<ITransceiver> transceivers)
        {
            _nativePtr = nativePtr;
            Options = new ChannelRTOptionsConfigurator(options);
            var dic = new Dictionary<ZlgFrameType, IZlgTransceiver>();
            
            var zlgTransceivers = transceivers.OfType<IZlgTransceiver>();
            
            foreach(var item in zlgTransceivers)
                dic.Add(item.FrameType, item);
            _transceivers = dic;
        }

        public void Start()
        {
            ThrowIfDisposed();

            //TODO: 异常处理
            if (ZLGCAN.ZCAN_StartCAN(_nativePtr) == 0)
                throw new Exception();
            _isOpen = true;
        }

        public void Reset()
        {
            ThrowIfDisposed();

            //TODO: 异常处理
            ZLGCAN.ZCAN_ResetCAN(_nativePtr);

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
            ZLGCAN.ZCAN_ClearBuffer(_nativePtr);
        }

        public uint Transmit(params CanFrameBase[] frames)
        {
            throw new NotImplementedException();
        }
        public IEnumerable<CanReceiveData> ReceiveAll(CanFrameType filterType)
        {
            ThrowIfDisposed();

            var count = CanReceiveCount(filterType);

            if (count == 0)
                return [];
            var zlgFilterType = filterType switch
            {
                CanFrameType.Any => ZlgFrameType.Any,
                CanFrameType.CanFd => ZlgFrameType.CanFd,
                CanFrameType.CanClassic => ZlgFrameType.CanClassic,
                CanFrameType.Lin => ZlgFrameType.Lin,
                _ => throw new NotSupportedException("ZlgCan 仅支持 Can/CanFD/Any/Lin")
            };
            if (_transceivers.TryGetValue(zlgFilterType, out var transceiver))
                return transceiver.Receive(this, count);
            
            throw new NotSupportedException($"{filterType} not supported");
        }
        public IEnumerable<CanReceiveData> Receive(CanFrameType filterType, uint count = 1, int timeOut = -1)
        {
            ThrowIfDisposed();
            var zlgFilterType = filterType switch
            {
                CanFrameType.Any => ZlgFrameType.Any,
                CanFrameType.CanFd => ZlgFrameType.CanFd,
                CanFrameType.CanClassic => ZlgFrameType.CanClassic,
                CanFrameType.Lin => ZlgFrameType.Lin,
                _ => throw new NotSupportedException("ZlgCan 仅支持 Can/CanFD/Any/Lin")
            };
            if (_transceivers.TryGetValue(zlgFilterType, out var transceiver))
                return transceiver.Receive(this, count, timeOut);
            throw new NotSupportedException($"{filterType} not supported");
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
            return ZLGCAN.ZCAN_GetReceiveNum(_nativePtr, (byte)zlgFilterType);
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

        public IntPtr NativePtr => _nativePtr;

        public ChannelRTOptionsConfigurator Options { get; }

        private readonly IntPtr _nativePtr;
        
        private readonly IReadOnlyDictionary<ZlgFrameType, IZlgTransceiver>  _transceivers;

        private bool _isDisposed;

        private bool _isOpen;
    }
}
