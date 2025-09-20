using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions;

/// <summary>
/// Optional dynamic feature provider. Implement to surface runtime-detected features
/// that depend on the actual device/bus instance (e.g., via netlink, driver query).
/// ZH: 可选的动态功能提供接口，用于在创建设备/总线时按实际实例（如 netlink/驱动查询）获取能力。
/// </summary>
public interface ICanDynamicFeatureProvider
{
    /// <summary>
    /// Get dynamic features for the device instance based on device options.
    /// ZH: 基于设备选项获取该实例的动态功能。
    /// </summary>
    /// <param name="options">Device options.</param>
    /// <returns>Dynamic feature flags.</returns>
    CanFeature GetDynamicDeviceFeatures(IDeviceOptions options);

    /// <summary>
    /// Get dynamic features for the bus/channel instance based on channel options.
    /// ZH: 基于通道/总线选项获取该实例的动态功能。
    /// </summary>
    /// <param name="options">Channel options.</param>
    /// <returns>Dynamic feature flags.</returns>
    CanFeature GetDynamicChannelFeatures(IChannelOptions options);
}

