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
    public sealed class ZlgCanChannel : ICanChannel<ZlgChannelRTConfigurator>
    {

        internal ZlgCanChannel(ZlgChannelHandle nativeHandle, IChannelOptions options, IEnumerable<ITransceiver> transceivers, CanFeature canFeature)
        {
            _nativeHandle = nativeHandle;
            Options = new ZlgChannelRTConfigurator();
            Options.Init((ZlgChannelOptions)options, canFeature);
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

            return _transceivers[ZlgFrameType.CanClassic].Transmit(this, frames);
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
        

        private readonly ZlgChannelHandle _nativeHandle;
        
        private readonly IReadOnlyDictionary<ZlgFrameType, IZlgTransceiver>  _transceivers;

        private bool _isDisposed;

        private bool _isOpen;
    }
}
