using System;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Diagnostics;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Native;

namespace Pkuyo.CanKit.ZLG
{
     public abstract class ZlgCanDevice(CanDeviceInfo info) : ICanDevice
     { 
        public bool OpenDevice()
        {
            ThrowIfDisposed();

            _nativePtr = ZLGCAN.ZCAN_OpenDevice((uint)DeviceInfo.DeviceType, DeviceInfo.DeviceIndex, 0);
            return IsDeviceOpen;
        }

        public void CloseDevice()
        {
            ThrowIfDisposed();

            ZlgErr.ThrowIfError(ZLGCAN.ZCAN_CloseDevice(_nativePtr));
        }


        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new InvalidOperationException();
        }
        
        
        public virtual void Dispose()
        {
            try
            {
                ThrowIfDisposed();
                
                if (_nativePtr != IntPtr.Zero)
                {
                    CloseDevice();
                }
            }
            finally
            {
                _nativePtr = IntPtr.Zero;
                _isDisposed = true;
            }
        }
        
        public CanDeviceInfo DeviceInfo => info;

        public IntPtr NativePtr => _nativePtr;

        public bool IsDeviceOpen => _nativePtr != IntPtr.Zero;

        private IntPtr _nativePtr;

        private bool _isDisposed;
    }
}