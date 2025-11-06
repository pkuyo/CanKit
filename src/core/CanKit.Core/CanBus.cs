using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Core.Definitions;
using CanKit.Core.Endpoints;
using CanKit.Core.Exceptions;
using CanKit.Core.Registry;
using CanKit.Core.Utils;

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
    /// Get capabilities by endpoint without opening the bus.
    /// ZH: 通过 Endpoint 在不打开通道的前提下嗅探能力。
    /// </summary>
    public static Capability QueryCapabilities(string endpoint, Action<IBusInitOptionsConfigurator>? configure = null)
    {
        if (!BusEndpointEntry.TryPrepare(endpoint, configure, out var prepared) || prepared == null)
            throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"No endpoint handler registered for '{endpoint}'.");

        // Prefer provider-level sniffer; fallback to static features
        if (prepared.Provider is ICanCapabilityProvider sniffer)
        {
            return sniffer.QueryCapabilities(prepared.BusOptions);
        }

        return new Capability(prepared.Provider.StaticFeatures);
    }

    /// <summary>
    /// Open a bus by DeviceType + index (以设备类型+索引打开总线)，返回已打开的总线并托管设备生命周期。
    /// </summary>
    public static ICanBus Open(DeviceType deviceType, Action<IBusInitOptionsConfigurator>? configure = null)
    {
        return Open<ICanBus, IBusOptions, IBusInitOptionsConfigurator>(deviceType, configure);
    }

    /// <summary>
    /// Get capabilities by DeviceType + init config without opening.
    /// ZH: 通过设备类型 + 初始配置在不打开的情况下嗅探能力。
    /// </summary>
    public static Capability QueryCapabilities(DeviceType deviceType,
        Action<IDeviceInitOptionsConfigurator>? configureDevice = null,
        Action<IBusInitOptionsConfigurator>? configureChannel = null)
    {
        var provider = CanRegistry.Registry.Resolve(deviceType);
        var (devOpt, devCfg) = provider.GetDeviceOptions();
        var (chOpt, chCfg) = provider.GetChannelOptions();
        configureDevice?.Invoke(devCfg);
        configureChannel?.Invoke(chCfg);

        if (provider is ICanCapabilityProvider capabilityProvider)
        {
            return capabilityProvider.QueryCapabilities(chOpt);
        }

        return new Capability(provider.StaticFeatures);
    }

    public static TBus Open<TBus, TBusOptions, TInitCfg>(ICanDevice device, TBusOptions options,
        TInitCfg cfg)
        where TBus : class, ICanBus
        where TBusOptions : class, IBusOptions
        where TInitCfg : IBusInitOptionsConfigurator
    {
        var provider = CanRegistry.Registry.Resolve(device.Options.DeviceType);
        var transceiver = provider.Factory.CreateTransceivers(device.Options, cfg);
        if (transceiver == null)
        {
            throw new CanFactoryException(CanKitErrorCode.TransceiverMismatch, $"Factory '{provider.Factory.GetType().FullName}' returned null transceiver.");
        }
        var channel = provider.Factory.CreateBus(device, options, transceiver, provider);
        if (channel == null)
        {
            throw new CanBusCreationException($"Factory '{provider.Factory.GetType().FullName}' returned null can bus.");
        }

        if (channel is not TBus typedBus)
        {
            throw new CanBusCreationException($"Factory produced bus type '{channel.GetType().FullName}' which cannot be assigned to '{typeof(TBus).FullName}'.");
        }

        // Attach lifetime so disposing bus also disposes device
        if (typedBus is IOwnership own)
        {
            own.AttachOwner(new DeviceOwner(device));
        }

        return typedBus;
    }

    /// <summary>
    /// Open a typed bus (打开强类型总线)，返回已打开的实例并托管设备生命周期。
    /// </summary>
    public static TBus Open<TBus, TBusOptions, TInitCfg>(DeviceType deviceType,
        Action<TInitCfg>? configure = null)
        where TBus : class, ICanBus
        where TBusOptions : class, IBusOptions
        where TInitCfg : IBusInitOptionsConfigurator
    {
        var provider = CanRegistry.Registry.Resolve(deviceType);

        var (deviceOptions, _) = provider.GetDeviceOptions();
        var (chOptions, chInitCfg) = provider.GetChannelOptions();

        if (chOptions is not TBusOptions typedChOptions)
        {
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(TBusOptions),
                chOptions?.GetType() ?? typeof(IBusOptions),
                $"channel");
        }

        if (chInitCfg is not TInitCfg typedInitCfg)
        {
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(TInitCfg),
                chInitCfg?.GetType() ?? typeof(IBusInitOptionsConfigurator),
                $"channel configurator");
        }

        configure?.Invoke(typedInitCfg);

        var device = provider.Factory.CreateDevice(deviceOptions);
        if (device == null)
        {
            throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"Factory '{provider.Factory.GetType().FullName}' returned null device.");
        }

        return Open<TBus, TBusOptions, TInitCfg>(device, typedChOptions, typedInitCfg);
    }


    public static QueuedCanBus WithQueuedTx(this ICanBus inner, QueuedCanBusOptions? options = null)
    {
        options ??= new QueuedCanBusOptions();
        return new QueuedCanBus(inner, options);
    }

    private sealed class DeviceOwner(ICanDevice device) : IDisposable
    {
        private ICanDevice? _device = device;

        public void Dispose()
        {
            try
            {
                _device?.Dispose();
            }
            finally
            {
                _device = null;
            }
        }
    }
}
