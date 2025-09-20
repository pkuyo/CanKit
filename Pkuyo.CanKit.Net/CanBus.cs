using System;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Endpoints;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.Core.Registry;

namespace Pkuyo.CanKit.Net;

/// <summary>
/// Facade to open a ready-to-use bus (用于打开可直接使用的总线门面)。
/// </summary>
public static class CanBus
{
    /// <summary>
    /// Open a bus by endpoint (通过 Endpoint 打开总线)，例如 "socketcan://can0" 或
    /// "zlg://USBCANFD-200U?index=0#ch1"。各 Provider 需通过 BusEndpointRegistry.Register 注册 scheme。
    /// </summary>
    public static ICanBus Open(string endpoint, Action<IChannelInitOptionsConfigurator>? configure = null)
    {
        if (BusEndpointRegistry.TryOpen(endpoint, configure, out var bus) && bus != null)
            return bus;
        throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"No endpoint handler registered for '{endpoint}'.");
    }
    /// <summary>
    /// Open a bus by DeviceType + index (以设备类型+索引打开总线)，返回已打开的总线并托管设备生命周期。
    /// </summary>
    public static ICanChannel Open(DeviceType deviceType, int channelIndex, Action<IChannelInitOptionsConfigurator>? configure = null)
    {
        return Open<ICanChannel, IChannelOptions, IChannelInitOptionsConfigurator>(deviceType, channelIndex, configure);
    }

    /// <summary>
    /// Open a typed bus (打开强类型总线)，返回已打开的实例并托管设备生命周期。
    /// </summary>
    public static TChannel Open<TChannel, TChannelOptions, TInitCfg>(DeviceType deviceType, int channelIndex,
        Action<TInitCfg>? configure = null)
        where TChannel : class, ICanChannel
        where TChannelOptions : class, IChannelOptions
        where TInitCfg : IChannelInitOptionsConfigurator
    {
        var provider = CanRegistry.Registry.Resolve(deviceType);

        var (deviceOptions, _) = provider.GetDeviceOptions();
        var (chOptions, chInitCfg) = provider.GetChannelOptions(channelIndex);

        if (chOptions is not TChannelOptions typedChOptions)
        {
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(TChannelOptions),
                chOptions?.GetType() ?? typeof(IChannelOptions),
                $"channel {channelIndex}");
        }

        if (chInitCfg is not TInitCfg typedInitCfg)
        {
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(TInitCfg),
                chInitCfg?.GetType() ?? typeof(IChannelInitOptionsConfigurator),
                $"channel {channelIndex} configurator");
        }

        configure?.Invoke(typedInitCfg);

        var device = provider.Factory.CreateDevice(deviceOptions);
        if (device == null)
        {
            throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"Factory '{provider.Factory.GetType().FullName}' returned null device.");
        }

        device.OpenDevice();

        var transceiver = provider.Factory.CreateTransceivers(device.Options, typedInitCfg);
        if (transceiver == null)
        {
            throw new CanFactoryException(CanKitErrorCode.TransceiverMismatch, $"Factory '{provider.Factory.GetType().FullName}' returned null transceiver.");
        }

        var channel = provider.Factory.CreateChannel(device, typedChOptions, transceiver);
        if (channel == null)
        {
            throw new CanChannelCreationException($"Factory '{provider.Factory.GetType().FullName}' returned null channel.");
        }

        if (channel is not TChannel typedChannel)
        {
            throw new CanChannelCreationException($"Factory produced channel type '{channel.GetType().FullName}' which cannot be assigned to '{typeof(TChannel).FullName}'.");
        }

        // Attach lifetime so disposing channel also disposes device
        if (typedChannel is IChannelOwnership own)
        {
            own.AttachOwner(new DeviceOwner(device));
        }

        typedChannel.Open();
        return typedChannel;
    }

    private sealed class DeviceOwner(ICanDevice device) : IDisposable
    {
        private ICanDevice? _device = device;

        public void Dispose()
        {
            try
            {
                _device?.CloseDevice();
                _device?.Dispose();
            }
            finally
            {
                _device = null;
            }
        }
    }
}
