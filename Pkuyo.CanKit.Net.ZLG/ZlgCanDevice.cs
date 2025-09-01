using System;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Diagnostics;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Native;
using Pkuyo.CanKit.ZLG.Options;

namespace Pkuyo.CanKit.ZLG
{
     public class ZlgCanDevice : ICanDevice<ZlgDeviceRTOptionsConfigurator>, ICanApplier
     {
         public ZlgCanDevice(IDeviceOptions options)
         {
             Options = new ZlgDeviceRTOptionsConfigurator();
             Options.Init((ZlgDeviceOptions)options, options.Provider.Features);
             _options = options;
         }
        public bool OpenDevice()
        {
            ThrowIfDisposed();
    
            var ptr = ZLGCAN.ZCAN_OpenDevice((uint)Options.DeviceType.NativeCode, Options.DeviceIndex, 0);
            _options.Apply(this, true);
            _nativePtr = ptr;
            return IsDeviceOpen;
        }

        public void CloseDevice()
        {
            ThrowIfDisposed();

            ZlgErr.ThrowIfError(ZLGCAN.ZCAN_CloseDevice(_nativePtr));
            _nativePtr = IntPtr.Zero;
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
        

        public IntPtr NativePtr => _nativePtr;

        public bool IsDeviceOpen => _nativePtr != IntPtr.Zero;
        
        public ZlgDeviceRTOptionsConfigurator Options { get; }

        private IDeviceOptions _options;

        private IntPtr _nativePtr;

        private bool _isDisposed;
        public bool ApplyOne<T>(string name, T value)
        {
            if (name[0] == '/')
            {
                ZLGCAN.ZCAN_SetValue(NativePtr,
                    Options.DeviceIndex.ToString() + name[0], value.ToString());
                return true;
            }

            return false;
        }

        public CanOptionType ApplierStatus => IsDeviceOpen ? CanOptionType.Runtime : CanOptionType.Init;
     }
}