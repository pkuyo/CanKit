using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions;

/// <summary>
///     定义用于构建 CAN 相关实例的工厂接口。
/// </summary>
public interface ICanFactory
{ 
    /// <summary>
    ///     根据提供的配置创建一个设备实例。
    /// </summary>
    /// <param name="options">用于初始化设备的配置。</param>
    /// <returns>创建完成的 <see cref="ICanDevice"/>。</returns>
    ICanDevice CreateDevice(IDeviceOptions options);

    /// <summary>
    ///     基于指定的设备和配置创建一个通道实例。
    /// </summary>
    /// <param name="device">通道依附的设备实例。</param>
    /// <param name="options">通道初始化所需的配置。</param>
    /// <param name="transceiver">与通道配套使用的收发器。</param>
    /// <returns>创建完成的 <see cref="ICanChannel"/>。</returns>
    ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, ITransceiver transceiver);

    /// <summary>
    ///     创建一个与指定配置匹配的收发器实例。
    /// </summary>
    /// <param name="deviceOptions">设备运行时配置访问器。</param>
    /// <param name="channelOptions">通道初始化配置访问器。</param>
    /// <returns>构建完成的 <see cref="ITransceiver"/>。</returns>
    ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions,
        IChannelInitOptionsConfigurator channelOptions);

    /// <summary>
    ///     判断当前工厂是否支持指定类型的设备。
    /// </summary>
    /// <param name="deviceType">待检测的设备类型。</param>
    /// <returns>若支持该设备类型则返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    bool Support(DeviceType deviceType);
}
