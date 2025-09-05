using System;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
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
             Options.Init((ZlgDeviceOptions)options);
             _options = options;
         }
         
        public bool OpenDevice()
        {
            ThrowIfDisposed();
            var ptr = ZLGCAN.ZCAN_OpenDevice(ZLGCAN.ZCAN_USBCAN2,0, 0);
            var handle = new ZlgDeviceHandle(ptr);
            if (handle is { IsInvalid: false })
            {
                _nativeHandler = handle;
                _options.Apply(this, true);
                isDeviceOpen = true;
                return IsDeviceOpen;
            }

            return false;
        }

        public void CloseDevice()
        {
            ThrowIfDisposed();
            isDeviceOpen = false;
            _nativeHandler.Close();
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
                _nativeHandler?.Dispose();
            }
            finally
            {
                isDeviceOpen = false;
                _isDisposed = true;
            }
        }
        

        public ZlgDeviceHandle NativeHandler => _nativeHandler;

        public bool IsDeviceOpen => isDeviceOpen;
        
        public ZlgDeviceRTOptionsConfigurator Options { get; }

        private bool isDeviceOpen = false;

        private IDeviceOptions _options;

        private ZlgDeviceHandle _nativeHandler;

        private bool _isDisposed;
        public bool ApplyOne<T>(string name, T value)
        {
            return ZLGCAN.ZCAN_SetValue(NativeHandler,
                Options.DeviceIndex.ToString() + name[0], value.ToString()) != 0;
        }

        public void Apply(ICanOptions options)
        {
           
        }

        public CanOptionType ApplierStatus => IsDeviceOpen ? CanOptionType.Runtime : CanOptionType.Init;
        
        IDeviceRTOptionsConfigurator ICanDevice.Options => Options;
     }
}