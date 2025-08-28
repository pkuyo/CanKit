using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Diagnostics;
using ZlgCAN.Net.Core.Models;
using ZlgCAN.Net.Core.Utils;
using ZlgCAN.Net.Native;

namespace ZlgCAN.Net.Core.Channels
{
    public abstract class CanChannel : ICanChannel
    {

        internal CanChannel(IntPtr nativePtr, CanChannelConfig config)
        {
            _nativePtr = nativePtr;
            Config = config;
        }

        public void Start()
        {
            if (_isDisposed)
                throw new InvalidOperationException();

            //TODO: 异常处理
            if (ZLGCAN.ZCAN_StartCAN(_nativePtr) == 0)
                throw new Exception();
             isOpen = true;
            
        }

        public void Reset()
        {
            if (_isDisposed)
                throw new InvalidOperationException();

            //TODO: 异常处理
            ZLGCAN.ZCAN_ResetCAN(_nativePtr);

            isOpen = false;
        }

        public void CleanBuffer()
        {
            if (_isDisposed)
                throw new InvalidOperationException();

            //TODO: 异常处理
            ZLGCAN.ZCAN_ClearBuffer(_nativePtr);
        }

        public bool IsOpen => isOpen;

        public IntPtr NativePtr => _nativePtr;

        public abstract CanFrameFlag SupportFlag { get; }

        public CanChannelConfig Config { get; }

        public uint Transmit(params CanFrameBase[] frames)
        {
            if (_isDisposed)
                throw new InvalidOperationException();

            var p2zcanDataObj = ZlgNativeExtension.TransmitCanFrames(frames, (byte)Config.ChannelIndex);
            var re = ZLGCAN.ZCAN_TransmitData(_nativePtr,
                p2zcanDataObj, 
                (uint)frames.Length);
            Marshal.FreeHGlobal(p2zcanDataObj);
            return re;
        }

        public IEnumerable<CanReceiveData> ReceiveAll(CanFrameFlag filterFlag = CanFrameFlag.Any)
        {
            if (_isDisposed)
                throw new InvalidOperationException();

            ZlgErr.ThrowIfNotSupport(this, filterFlag);

            var count = CanReceiveCount(filterFlag);

            IntPtr p2zcanReceiveData = Marshal.AllocHGlobal((int)(Marshal.SizeOf(typeof(ZLGCAN.ZCANDataObj)) * count));
            var recCount = ZLGCAN.ZCAN_ReceiveData(_nativePtr, p2zcanReceiveData, count, -1);
            return ZlgNativeExtension.RecvCanFrames(p2zcanReceiveData, (int)recCount, filterFlag);
        }

        public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = -1, CanFrameFlag filterFlag = CanFrameFlag.Any)
        {
            if (_isDisposed || count < 1)
                throw new InvalidOperationException();

            ZlgErr.ThrowIfNotSupport(this, filterFlag);

            IntPtr p2zcanReceiveData = Marshal.AllocHGlobal((int)(Marshal.SizeOf(typeof(ZLGCAN.ZCANDataObj)) * count));
            var recCount = ZLGCAN.ZCAN_ReceiveData(_nativePtr, p2zcanReceiveData, count, timeOut);
            return ZlgNativeExtension.RecvCanFrames(p2zcanReceiveData, (int)recCount, filterFlag);

        }

        public uint CanReceiveCount(CanFrameFlag filterFlag)
        {
            if (_isDisposed)
                throw new InvalidOperationException();

            ZlgErr.ThrowIfNotSupport(this, filterFlag);
            if(filterFlag == CanFrameFlag.Error)
                throw new NotSupportedException(); //TODO

            if (filterFlag == CanFrameFlag.Any)
                filterFlag = (CanFrameFlag)2;

            if((uint)filterFlag > 2)
                throw new NotSupportedException(); //TODO

            return ZLGCAN.ZCAN_GetReceiveNum(_nativePtr, (byte)filterFlag);
        }

        public void Dispose()
        {
            try
            {
                if (_isDisposed)
                    throw new InvalidOperationException();
                Reset();
            }
            finally
            {
                _isDisposed = true;
            }
      
        }

       

        private IntPtr _nativePtr;

        private bool _isDisposed;

        private bool isOpen;
    }
}
