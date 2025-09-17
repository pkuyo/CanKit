using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{
    
    /// <summary>
    ///     定义提供特定设备型号及其配置能力的提供程序接口。
    /// </summary>
    public interface ICanModelProvider
    {
        /// <summary>
        ///     获取当前模型对应的设备类型。
        /// </summary>
        DeviceType DeviceType { get; }

        /// <summary>
        ///     获取模型支持的功能集合。
        /// </summary>
        CanFeature Features { get; }

        /// <summary>
        ///     获取用于创建设备、通道以及收发器的工厂实例。
        /// </summary>
        ICanFactory Factory { get; }

        /// <summary>
        ///     创建默认的设备配置对象及其初始化配置器。
        /// </summary>
        /// <returns>包含设备配置以及初始化配置器的二元组。</returns>
        (IDeviceOptions,IDeviceInitOptionsConfigurator) GetDeviceOptions();

        /// <summary>
        ///     为指定索引的通道创建配置对象与初始化配置器。
        /// </summary>
        /// <param name="channelIndex">目标通道的索引。</param>
        /// <returns>包含通道配置以及初始化配置器的二元组。</returns>
        (IChannelOptions,IChannelInitOptionsConfigurator) GetChannelOptions(int channelIndex);

    }
}
