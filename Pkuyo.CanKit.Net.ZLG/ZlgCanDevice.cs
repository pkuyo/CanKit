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
    
            var handle = ZLGCAN.ZCAN_OpenDevice((uint)(int)Options.DeviceType.Metadata, Options.DeviceIndex, 0);
            if (handle != null)
            {
                _options.Apply(this, true);
                _nativeHandler = handle;
                return IsDeviceOpen;
            }

            return false;
        }

        public void CloseDevice()
        {
            ThrowIfDisposed();
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
                _nativeHandler.Dispose();
            }
            finally
            {
                _isDisposed = true;
            }
        }
        

        public ZlgDeviceHandle NativeHandler => _nativeHandler;

        public bool IsDeviceOpen => _nativeHandler is { IsInvalid: false, IsClosed: false };
        
        public ZlgDeviceRTOptionsConfigurator Options { get; }

        private IDeviceOptions _options;

        private ZlgDeviceHandle _nativeHandler;

        private bool _isDisposed;
        public bool ApplyOne<T>(string name, T value)
        {
            if (name[0] == '/')
            {
                ZLGCAN.ZCAN_SetValue(NativeHandler,
                    Options.DeviceIndex.ToString() + name[0], value.ToString());
                return true;
            }

            return false;
        }

        public CanOptionType ApplierStatus => IsDeviceOpen ? CanOptionType.Runtime : CanOptionType.Init;
     }
}