using System.Collections.Generic;
using CanKit.Core.Definitions;

namespace CanKit.Core.Abstractions
{

    /// <summary>
    /// Provides model metadata and factories for a specific device family (为特定设备族提供模型与工厂)。
    /// </summary>
    public interface ICanModelProvider
    {
        /// <summary>
        /// Device type of this model (对应的设备类型)。
        /// </summary>
        DeviceType DeviceType { get; }

        /// <summary>
        /// Supported features (支持的功能集合)。
        /// </summary>
        CanFeature StaticFeatures { get; }

        /// <summary>
        /// Factory for device/channel/transceiver (创建设备/通道/收发器的工厂)。
        /// </summary>
        ICanFactory Factory { get; }

        /// <summary>
        /// Get default device options and initializer (获取默认设备选项与初始化器)。
        /// </summary>
        /// <returns>Tuple of options and configurator (选项与配置器)。</returns>
        (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions();

        /// <summary>
        /// Get channel options and initializer for index (为指定通道索引获取选项与初始化器)。
        /// </summary>
        /// <param name="channelIndex">Channel index (通道索引)。</param>
        /// <returns>Tuple of options and configurator (选项与配置器)。</returns>
        (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions(int channelIndex);

    }

    /// <summary>
    /// Group provider that can supply model providers for multiple device types.
    /// ZH: 分组提供者接口：为多个设备类型按需创建具体的模型提供者。
    /// </summary>
    public interface ICanModelProviderGroup
    {
        /// <summary>
        /// Device types supported by this group.
        /// ZH: 本分组支持的设备类型集合。
        /// </summary>
        IEnumerable<DeviceType> SupportedDeviceTypes { get; }

        /// <summary>
        /// Create a concrete provider for the specified device type.
        /// ZH: 根据指定设备类型创建对应的模型提供者。
        /// </summary>
        /// <param name="deviceType">Target device type. ZH: 目标设备类型。</param>
        /// <returns>Concrete provider. ZH: 具体的模型提供者。</returns>
        ICanModelProvider Create(DeviceType deviceType);
    }
}
