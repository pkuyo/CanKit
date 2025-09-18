using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Exceptions;

namespace Pkuyo.CanKit.Net.Core;

/// <summary>
/// 表示一个 CAN 会话，用于管理底层设备的打开/关闭以及通道的创建、销毁和缓存。
/// </summary>
/// <typeparam name="TCanDevice">具体的 CAN 设备实现类型，必须实现 <see cref="ICanDevice"/> 接口。</typeparam>
/// <typeparam name="TCanChannel">具体的 CAN 通道实现类型，必须实现 <see cref="ICanChannel"/> 接口。</typeparam>
/// <param name="device">与会话关联的设备实例。</param>
/// <param name="provider">提供 CAN 模型配置与工厂的提供者。</param>
public class CanSession<TCanDevice, TCanChannel>(TCanDevice device, ICanModelProvider provider) : IDisposable
    where TCanDevice : class, ICanDevice
    where TCanChannel : class, ICanChannel
{
    /// <summary>
    /// 根据索引获取已经创建的通道。
    /// </summary>
    /// <param name="index">通道索引。</param>
    /// <returns>与索引对应的通道实例。</returns>
    public TCanChannel this[int index] => InnerChannels[index];

    /// <summary>
    /// 打开底层 CAN 设备。
    /// </summary>
    public void Open()
    {
        Device.OpenDevice();
    }

    /// <summary>
    /// 关闭底层 CAN 设备。
    /// </summary>
    public void Close()
    {
        Device.CloseDevice();
    }

    /// <summary>
    /// 使用指定波特率创建或返回缓存的通道。
    /// </summary>
    /// <param name="index">通道索引。</param>
    /// <param name="baudRate">期望的总线波特率。</param>
    /// <returns>创建或获取的 CAN 通道实例。</returns>
    public TCanChannel CreateChannel(int index, uint baudRate)
    {
        return CreateChannel<IChannelOptions, IChannelInitOptionsConfigurator>(index,
            cfg => cfg.Baud(baudRate));
    }

    /// <summary>
    /// 使用自定义配置创建或返回缓存的通道。
    /// </summary>
    /// <param name="index">通道索引。</param>
    /// <param name="configure">通道初始化选项的配置委托，可选。</param>
    /// <returns>创建或获取的 CAN 通道实例。</returns>
    public TCanChannel CreateChannel(int index, Action<IChannelInitOptionsConfigurator> configure = null)
    {
        return CreateChannel<IChannelOptions, IChannelInitOptionsConfigurator>(index, configure);
    }

    /// <summary>
    /// 使用指定的选项类型与配置器类型创建或返回缓存的通道。（不推荐直接使用）
    /// </summary>
    /// <typeparam name="TChannelOptions">通道选项的具体类型。</typeparam>
    /// <typeparam name="TOptionCfg">初始化配置器的具体类型。</typeparam>
    /// <param name="index">通道索引。</param>
    /// <param name="configure">对配置器进行个性化设置的委托，可选。</param>
    /// <returns>创建或获取的 CAN 通道实例。</returns>
    /// <exception cref="CanDeviceNotOpenException">设备尚未打开时抛出。</exception>
    /// <exception cref="CanOptionTypeMismatchException">提供的选项或配置器类型与工厂需求不匹配时抛出。</exception>
    /// <exception cref="CanFactoryException">工厂无法生成收发器时抛出。</exception>
    /// <exception cref="CanChannelCreationException">工厂返回空或类型不匹配的通道实例时抛出。</exception>
    protected TCanChannel CreateChannel<TChannelOptions, TOptionCfg>(int index,
        Action<TOptionCfg> configure = null)
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
    /// 销毁指定索引的通道并释放资源。
    /// </summary>
    /// <param name="channelIndex">要销毁的通道索引。</param>
    /// <exception cref="CanChannelNotOpenException">指定索引没有打开的通道时抛出。</exception>
    public void DestroyChannel(int channelIndex)
    {
        if (!InnerChannels.TryGetValue(channelIndex, out var channel))
            throw new CanChannelNotOpenException();
        channel.Dispose();

        InnerChannels.Remove(channelIndex);
    }

    /// <summary>
    /// 释放会话及其所有通道所占用的资源。
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
    /// 获取设备是否已经打开。
    /// </summary>
    public bool IsDeviceOpen => Device.IsDeviceOpen;

    /// <summary>
    /// 缓存已创建的通道实例，键为通道索引。
    /// </summary>
    protected readonly Dictionary<int, TCanChannel> InnerChannels = new();

    /// <summary>
    /// 底层设备实例。
    /// </summary>
    protected TCanDevice Device { get; } = device;

    /// <summary>
    /// 提供通道选项和工厂的模型提供者。
    /// </summary>
    protected ICanModelProvider Provider { get; } = provider;
}