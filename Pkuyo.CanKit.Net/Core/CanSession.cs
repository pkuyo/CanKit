using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Exceptions;

namespace Pkuyo.CanKit.Net.Core;

/// <summary>
/// Represents a CAN session (表示一个 CAN 会话)，管理设备打开/关闭及通道的创建与缓存。
/// </summary>
/// <typeparam name="TCanDevice">Concrete CAN device type (设备类型)。</typeparam>
/// <typeparam name="TCanChannel">Concrete CAN channel type (通道类型)。</typeparam>
/// <param name="device">Device instance bound to this session (与会话绑定的设备实例)。</param>
/// <param name="provider">Model provider for options and factory (提供模型与工厂的提供者)。</param>
public class CanSession<TCanDevice, TCanChannel>(TCanDevice device, ICanModelProvider provider) : IDisposable
    where TCanDevice : class, ICanDevice
    where TCanChannel : class, ICanChannel
{
    /// <summary>
    /// Get channel by index (按索引获取已创建的通道)。
    /// </summary>
    /// <param name="index">Channel index (通道索引)。</param>
    /// <returns>Channel instance (通道实例)。</returns>
    public TCanChannel this[int index] => InnerChannels[index];

    /// <summary>
    /// Open the underlying CAN device (打开底层 CAN 设备)。
    /// </summary>
    public void Open()
    {
        Device.OpenDevice();
    }

    /// <summary>
    /// Close the underlying CAN device (关闭底层 CAN 设备)。
    /// </summary>
    public void Close()
    {
        Device.CloseDevice();
    }

    /// <summary>
    /// Create or return cached channel with baud rate (使用波特率创建或返回通道)。
    /// </summary>
    /// <param name="index">Channel index (通道索引)。</param>
    /// <param name="baudRate">Bus bitrate (总线波特率)。</param>
    /// <returns>Channel instance (通道实例)。</returns>
    public TCanChannel CreateChannel(int index, uint baudRate)
    {
        return CreateChannel<IChannelOptions, IChannelInitOptionsConfigurator>(index,
            cfg => cfg.Baud(baudRate));
    }

    /// <summary>
    /// Create or return cached channel with custom config (使用自定义配置创建/返回通道)。
    /// </summary>
    /// <param name="index">Channel index (通道索引)。</param>
    /// <param name="configure">Initializer for channel options, optional (通道初始化配置委托，可选)。</param>
    /// <returns>Channel instance (通道实例)。</returns>
    public TCanChannel CreateChannel(int index, Action<IChannelInitOptionsConfigurator>? configure = null)
    {
        return CreateChannel<IChannelOptions, IChannelInitOptionsConfigurator>(index, configure);
    }

    /// <summary>
    /// Create or return cached channel with specific option/configurator types (不建议直接使用)。
    /// </summary>
    /// <typeparam name="TChannelOptions">Channel option type (通道选项类型)。</typeparam>
    /// <typeparam name="TOptionCfg">Initializer type (初始化配置器类型)。</typeparam>
    /// <param name="index">Channel index (通道索引)。</param>
    /// <param name="configure">Configurator callback, optional (配置器回调，可选)。</param>
    /// <returns>Channel instance (通道实例)。</returns>
    /// <exception cref="CanDeviceNotOpenException">Thrown if device is not open (设备未打开)。</exception>
    /// <exception cref="CanOptionTypeMismatchException">Thrown if option/configurator type mismatches (类型不匹配)。</exception>
    /// <exception cref="CanFactoryException">Thrown if transceiver cannot be created (无法创建收发器)。</exception>
    /// <exception cref="CanChannelCreationException">Thrown if channel is null or type mismatched (通道创建失败或类型不匹配)。</exception>
    protected TCanChannel CreateChannel<TChannelOptions, TOptionCfg>(int index,
        Action<TOptionCfg>? configure = null)
        where TChannelOptions : class, IChannelOptions
        where TOptionCfg : IChannelInitOptionsConfigurator
    {
        if (!IsDeviceOpen)
            throw new CanDeviceNotOpenException();

        if (InnerChannels.TryGetValue(index, out var channel) && channel != null)
            return channel;

        var (options, cfg) = Provider.GetChannelOptions(index);
        if (options is not TChannelOptions typedOptions)
        {
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(TChannelOptions),
                options?.GetType() ?? typeof(IChannelOptions),
                $"channel {index}");
        }

        if (cfg is not TOptionCfg specCfg)
        {
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(TOptionCfg),
                cfg?.GetType() ?? typeof(IChannelInitOptionsConfigurator),
                $"channel {index} configurator");
        }

        configure?.Invoke(specCfg);

        var transceivers = Provider.Factory.CreateTransceivers(Device.Options, specCfg);
        if (transceivers == null)
        {
            throw new CanFactoryException(
                CanKitErrorCode.TransceiverMismatch,
                $"Factory '{Provider.Factory.GetType().FullName}' returned null when creating a transceiver for channel {index}.");
        }

        var createdChannel = Provider.Factory.CreateChannel(Device, typedOptions, transceivers);
        if (createdChannel == null)
        {
            throw new CanChannelCreationException(
                $"Factory '{Provider.Factory.GetType().FullName}' returned null when creating channel {index}.");
        }

        if (createdChannel is not TCanChannel innerChannel)
        {
            throw new CanChannelCreationException(
                $"Factory produced channel type '{createdChannel.GetType().FullName}' which cannot be assigned to '{typeof(TCanChannel).FullName}'.");
        }

        InnerChannels.Add(index, innerChannel);
        return innerChannel;

    }

    /// <summary>
    /// Destroy channel by index and release resources (销毁指定索引的通道并释放资源)。
    /// </summary>
    /// <param name="channelIndex">Channel index (通道索引)。</param>
    /// <exception cref="CanChannelNotOpenException">Thrown if channel not open (通道未打开)。</exception>
    public void DestroyChannel(int channelIndex)
    {
        if (!InnerChannels.TryGetValue(channelIndex, out var channel))
            throw new CanChannelNotOpenException();
        channel.Dispose();

        InnerChannels.Remove(channelIndex);
    }

    /// <summary>
    /// Dispose session and all channels (释放会话及其所有通道的资源)。
    /// </summary>
    public void Dispose()
    {
        Device.Dispose();
        foreach (var channel in InnerChannels)
        {
            channel.Value.Dispose();
        }

        InnerChannels.Clear();
    }

    /// <summary>
    /// Whether device is open (设备是否已打开)。
    /// </summary>
    public bool IsDeviceOpen => Device.IsDeviceOpen;

    /// <summary>
    /// Cache of created channels keyed by index (已创建通道的缓存)。
    /// </summary>
    protected readonly Dictionary<int, TCanChannel> InnerChannels = new();

    /// <summary>
    /// Underlying device instance (底层设备实例)。
    /// </summary>
    protected TCanDevice Device { get; } = device;

    /// <summary>
    /// Model provider for channel options and factory (提供通道选项与工厂的模型提供者)。
    /// </summary>
    protected ICanModelProvider Provider { get; } = provider;
}

