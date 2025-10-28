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

        private bool _isDisposed;

        private readonly ZlgDeviceHandle _nativeHandler = new();

        private readonly IDeviceOptions _options;

        public ZlgCanDevice(IDeviceOptions options)
        {
            Options = new ZlgDeviceRTOptionsConfigurator();
            Options.Init((ZlgDeviceOptions)options);
            _options = options;
            ThrowIfDisposed();
            var zdt = (ZlgDeviceType)Options.DeviceType;
            var ptr = ZLGCAN.ZCAN_OpenDevice((uint)zdt.Code, Options.DeviceIndex, 0);
            var handle = new ZlgDeviceHandle(ptr);
            if (handle is { IsInvalid: false })
            {
                _nativeHandler = handle;
                ApplyConfig(_options);
                return;
            }

            ZlgErr.ThrowIfInvalid(handle, nameof(ZLGCAN.ZCAN_OpenDevice));
        }


        public ZlgDeviceHandle NativeHandler => _nativeHandler;


        public void Dispose()
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
