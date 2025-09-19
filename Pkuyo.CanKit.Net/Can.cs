using System;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.Core.Registry;

namespace Pkuyo.CanKit.Net
{
    /// <summary>
    /// Entry point for CAN sessions (面向不同厂商设备的统一会话入口)。
    /// </summary>
    public static class Can
    {
        /// <summary>
        /// Open a generic CAN session (打开通用 CAN 会话)。
        /// </summary>
        /// <param name="deviceType">Target device type (目标设备类型)。</param>
        /// <param name="configure">Initializer for device options, optional (设备初始化配置委托，可选)。</param>
        public static CanSession<ICanDevice, ICanChannel> Open(DeviceType deviceType, Action<IDeviceInitOptionsConfigurator>? configure = null)
        {
            return Open<ICanDevice, ICanChannel, IDeviceOptions, IDeviceInitOptionsConfigurator>(deviceType, configure);
        }

        /// <summary>
        /// Generic open returning a strongly-typed session (返回强类型会话的泛型打开入口)。
        /// </summary>
        /// <typeparam name="TDevice">Concrete CAN device type (设备类型)。</typeparam>
        /// <typeparam name="TChannel">Concrete CAN channel type (通道类型)。</typeparam>
        /// <typeparam name="TDeviceOptions">Device option type (设备选项类型)。</typeparam>
        /// <typeparam name="TOptionCfg">Device option configurator type (设备配置器类型)。</typeparam>
        /// <param name="deviceType">Device type to open (要打开的设备类型)。</param>
        /// <param name="configure">Adjust initialization options before open (打开前配置初始化参数)。</param>
        /// <param name="sessionBuilder">Optional custom session builder (可选自定义会话构建器)。</param>
        /// <returns>Opened CAN session (已打开的 CAN 会话)。</returns>
        public static CanSession<TDevice, TChannel> Open<TDevice, TChannel, TDeviceOptions, TOptionCfg>(
            DeviceType deviceType,
            Action<TOptionCfg>? configure = null,
            Func<TDevice, ICanModelProvider, CanSession<TDevice, TChannel>>? sessionBuilder = null)
            where TDevice : class, ICanDevice
            where TChannel : class, ICanChannel
            where TDeviceOptions : class, IDeviceOptions
            where TOptionCfg : IDeviceInitOptionsConfigurator
        {
            // 根据设备类型从注册表解析出工厂及模型信息
            var provider = CanRegistry.Registry.Resolve(deviceType);
            var factory = provider.Factory;

            // 从提供方获取默认设备参数与配置器
            var (options, cfg) = provider.GetDeviceOptions();

            if (options is not TDeviceOptions typedOptions)
            {
                // 由调用方指定的目标类型与注册的默认类型不匹配，直接抛出异常
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

            // 将外部配置委托应用到具体的配置器上
            configure?.Invoke(specCfg);

            // 通过工厂创建设备实例
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

            // 默认使用框架提供的会话，也允许调用方注入定制实现
            var session = sessionBuilder == null
                ? new CanSession<TDevice, TChannel>(device, provider)
                : sessionBuilder(device, provider);

            // 立即打开会话，使设备步入可用状态
            session.Open();
            return session;
        }
    }
}

