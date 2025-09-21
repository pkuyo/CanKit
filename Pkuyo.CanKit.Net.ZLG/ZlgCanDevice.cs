using System;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Diagnostics;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Diagnostics;
using Pkuyo.CanKit.ZLG.Exceptions;
using Pkuyo.CanKit.ZLG.Native;
using Pkuyo.CanKit.ZLG.Options;

namespace Pkuyo.CanKit.ZLG
{
     public sealed class ZlgCanDevice : ICanDevice<ZlgDeviceRTOptionsConfigurator>, INamedCanApplier
     {
         public ZlgCanDevice(IDeviceOptions options)
         {
             Options = new ZlgDeviceRTOptionsConfigurator();
             Options.Init((ZlgDeviceOptions)options);
             _options = options;
         }
         
        public void OpenDevice()
        {
            ThrowIfDisposed();
            var ptr = ZLGCAN.ZCAN_OpenDevice(Options.DeviceIndex,0, 0);
            var handle = new ZlgDeviceHandle(ptr);
            if (handle is { IsInvalid: false })
            {
                _nativeHandler = handle;
                _options.Apply(this, true);
                _isDeviceOpen = true;
                return;
            }

            ZlgErr.ThrowIfInvalid(handle, nameof(ZLGCAN.ZCAN_OpenDevice));
        }

        public void CloseDevice()
        {
            ThrowIfDisposed();
            _isDeviceOpen = false;
            _nativeHandler.Close();
        }


        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new CanDeviceDisposedException();
        }
        
        
        public void Dispose()
        {
            try
            {
            
                ThrowIfDisposed();
                _nativeHandler?.Dispose();
            }
            finally
            {
                _isDeviceOpen = false;
                _isDisposed = true;
            }
        }
        

        public ZlgDeviceHandle NativeHandler => _nativeHandler;

        public bool IsDeviceOpen => _isDeviceOpen;
        
        public ZlgDeviceRTOptionsConfigurator Options { get; }

        private bool _isDeviceOpen = false;

        private IDeviceOptions _options;

        private ZlgDeviceHandle _nativeHandler = new ();

        private bool _isDisposed;
        public bool ApplyOne<T>(object id, T value)
        {
            return ZLGCAN.ZCAN_SetValue(NativeHandler,
                Options.DeviceIndex + (string)id, value!.ToString()) != 0;
        }

        public void Apply(ICanOptions options)
        {
           
        }

        public CanOptionType ApplierStatus => IsDeviceOpen ? CanOptionType.Runtime : CanOptionType.Init;
        
        IDeviceRTOptionsConfigurator ICanDevice.Options => Options;
     }
}