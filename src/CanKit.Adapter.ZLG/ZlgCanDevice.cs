using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Diagnostics;
using CanKit.Adapter.ZLG.Native;
using CanKit.Adapter.ZLG.Options;
using CanKit.Core.Abstractions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.ZLG
{
    public sealed class ZlgCanDevice : ICanDevice<ZlgDeviceRTOptionsConfigurator>
    {
        private bool _isDeviceOpen = false;

        private bool _isDisposed;

        private ZlgDeviceHandle _nativeHandler = new();

        private IDeviceOptions _options;

        public ZlgCanDevice(IDeviceOptions options)
        {
            Options = new ZlgDeviceRTOptionsConfigurator();
            Options.Init((ZlgDeviceOptions)options);
            _options = options;
        }


        public ZlgDeviceHandle NativeHandler => _nativeHandler;

        public void OpenDevice()
        {
            ThrowIfDisposed();
            var zdt = (ZlgDeviceType)Options.DeviceType;
            var ptr = ZLGCAN.ZCAN_OpenDevice((uint)zdt.Code, Options.DeviceIndex, 0);
            var handle = new ZlgDeviceHandle(ptr);
            if (handle is { IsInvalid: false })
            {
                _nativeHandler = handle;
                ApplyConfig(_options);
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

        public bool IsDeviceOpen => _isDeviceOpen;

        public ZlgDeviceRTOptionsConfigurator Options { get; }

        IDeviceRTOptionsConfigurator ICanDevice.Options => Options;


        public void ApplyConfig(ICanOptions options)
        {

        }


        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new CanDeviceDisposedException();
        }
    }
}
