using System;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.Core.Registry;

namespace Pkuyo.CanKit.Net
{
    public static class Can
    {
        public static CanSession<ICanDevice, ICanChannel> Open(DeviceType deviceType, Action<IDeviceInitOptionsConfigurator> configure = null)
        {
            return Open<ICanDevice, ICanChannel, IDeviceOptions, IDeviceInitOptionsConfigurator>(deviceType, configure);
        }

        public static CanSession<TDevice, TChannel> Open<TDevice, TChannel, TDeviceOptions, TOptionCfg>(
            DeviceType deviceType,
            Action<TOptionCfg> configure = null,
            Func<TDevice, ICanModelProvider, CanSession<TDevice, TChannel>> sessionBuilder = null)
            where TDevice : class, ICanDevice
            where TChannel : class, ICanChannel
            where TDeviceOptions : class, IDeviceOptions
            where TOptionCfg : IDeviceInitOptionsConfigurator
        {
            var provider = CanRegistry.Registry.Resolve(deviceType);
            var factory = provider.Factory;

            var (options, cfg) = provider.GetDeviceOptions();

            if (options is not TDeviceOptions typedOptions)
            {
                throw new CanOptionTypeMismatchException(
                    CanKitErrorCode.DeviceOptionTypeMismatch,
                    typeof(TDeviceOptions),
                    options?.GetType() ?? typeof(IDeviceOptions),
                    "device");
            }

            if (cfg is not TOptionCfg specCfg)
            {
                throw new CanOptionTypeMismatchException(
                    CanKitErrorCode.DeviceOptionTypeMismatch,
                    typeof(TOptionCfg),
                    cfg?.GetType() ?? typeof(IDeviceInitOptionsConfigurator),
                    "device configurator");
            }

            configure?.Invoke(specCfg);

            var createdDevice = factory.CreateDevice(typedOptions);
            if (createdDevice == null)
            {
                throw new CanFactoryException(
                    CanKitErrorCode.DeviceCreationFailed,
                    $"Factory '{factory.GetType().FullName}' failed to create a CAN device for '{deviceType.Id}'.");
            }

            if (createdDevice is not TDevice device)
            {
                throw new CanFactoryDeviceMismatchException(typeof(TDevice), createdDevice.GetType());
            }

            var session = sessionBuilder == null
                ? new CanSession<TDevice, TChannel>(device, provider)
                : sessionBuilder(device, provider);

            session.Open();
            return session;
        }
    }
}
