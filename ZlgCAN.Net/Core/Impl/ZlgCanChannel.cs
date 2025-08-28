using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Diagnostics;
using ZlgCAN.Net.Core.Definitions;
using ZlgCAN.Net.Core.Utils;
using ZlgCAN.Net.Native;

namespace ZlgCAN.Net.Core.Channels
{
    public sealed class CanChannel : ICanChannel
    {

        internal CanChannel(IntPtr nativePtr, IChannelRuntimeOptions options, IEnumerable<ITransceiver> transceivers)
        {
            _nativePtr = nativePtr;
            Options = options;
            var dic = new Dictionary<CanFilterType, ITransceiver>();
            foreach(var item in transceivers)
                dic.Add(item.FilterType, item);
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
        public IEnumerable<CanReceiveData> ReceiveAll(CanFilterType filterType)
        {
            ThrowIfDisposed();

            var count = CanReceiveCount(filterType);

            if (count == 0)
                return [];

            if (_transceivers.TryGetValue(filterType, out var transceiver))
                return transceiver.Receive(this, count);
            
            throw new NotSupportedException($"{filterType} not supported");
        }
        public IEnumerable<CanReceiveData> Receive(CanFilterType filterType, uint count = 1, int timeOut = -1)
        {
            ThrowIfDisposed();
            
            if (_transceivers.TryGetValue(filterType, out var transceiver))
                return transceiver.Receive(this, count, timeOut);
            throw new NotSupportedException($"{filterType} not supported");
        }
        
        

        public uint CanReceiveCount(CanFilterType filterType)
        {
            ThrowIfDisposed();
            
            if((uint)filterType > 2)
                throw new NotSupportedException("只有Can/CanFD/Any 可以在ReceiveCount传入");
            
            return ZLGCAN.ZCAN_GetReceiveNum(_nativePtr, (byte)filterType);
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

        public IChannelRuntimeOptions Options { get; }

        private readonly IntPtr _nativePtr;
        
        private readonly IReadOnlyDictionary<CanFilterType, ITransceiver>  _transceivers;

        private bool _isDisposed;

        private bool _isOpen;
    }
}
