using System;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Endpoints;
using CanKit.Core.Exceptions;
using CanKit.Core.Registry;

namespace CanKit.Core;

/// <summary>
/// Facade to open a ready-to-use bus (用于打开可直接使用的总线门面)。
/// </summary>
public static class CanBus
{
    /// <summary>
    /// Open a bus by endpoint (通过 Endpoint 打开总线)，例如 "socketcan://can0" 或
    /// "zlg://USBCANFD-200U?index=0#ch1"。各 Provider 需通过 BusEndpointRegistry.Register 注册 scheme。
    /// </summary>
    public static ICanBus Open(string endpoint, Action<IBusInitOptionsConfigurator>? configure = null)
    {
        if (BusEndpointEntry.TryOpen(endpoint, configure, out var bus) && bus != null)
            return bus;
        throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"No endpoint handler registered for '{endpoint}'.");
    }
    /// <summary>
    /// Open a bus by DeviceType + index (以设备类型+索引打开总线)，返回已打开的总线并托管设备生命周期。
    /// </summary>
    public static ICanBus Open(DeviceType deviceType, int channelIndex, Action<IBusInitOptionsConfigurator>? configure = null)
    {
        return Open<ICanBus, IBusOptions, IBusInitOptionsConfigurator>(deviceType, channelIndex, configure);
    }

    /// <summary>
    /// Open a typed bus (打开强类型总线)，返回已打开的实例并托管设备生命周期。
    /// </summary>
    public static TBus Open<TBus, TBusOptions, TInitCfg>(DeviceType deviceType, int channelIndex,
        Action<TInitCfg>? configure = null)
        where TBus : class, ICanBus
        where TBusOptions : class, IBusOptions
        where TInitCfg : IBusInitOptionsConfigurator
    {
        var provider = CanRegistry.Registry.Resolve(deviceType);

        var (deviceOptions, _) = provider.GetDeviceOptions();
        var (chOptions, chInitCfg) = provider.GetChannelOptions(channelIndex);

        if (chOptions is not TBusOptions typedChOptions)
        {
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(TBusOptions),
                chOptions?.GetType() ?? typeof(IBusOptions),
                $"channel {channelIndex}");
        }

        if (chInitCfg is not TInitCfg typedInitCfg)
        {
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(TInitCfg),
                chInitCfg?.GetType() ?? typeof(IBusInitOptionsConfigurator),
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

        var channel = provider.Factory.CreateBus(device, typedChOptions, transceiver);
        if (channel == null)
        {
            throw new CanBusCreationException($"Factory '{provider.Factory.GetType().FullName}' returned null can bus.");
        }

        if (channel is not TBus typedBus)
        {
            throw new CanBusCreationException($"Factory produced bus type '{channel.GetType().FullName}' which cannot be assigned to '{typeof(TBus).FullName}'.");
        }

        // Attach lifetime so disposing bus also disposes device
        if (typedBus is IBusOwnership own)
        {
            own.AttachOwner(new DeviceOwner(device));
        }

        return typedBus;
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
