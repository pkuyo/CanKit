using System;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Adapter.ControlCAN.Options;
using CanKit.Core.Exceptions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ControlCAN;

public static class ControlCan
{
    public static ControlCanBus Open(Definitions.ControlCanDeviceType deviceType, int deviceIndex, int channelIndex,
        Action<ControlCanBusInitConfigurator>? configure = null)
    {
        if (channelIndex < 0) throw new ArgumentOutOfRangeException(nameof(channelIndex));
        if (deviceIndex < 0) throw new ArgumentOutOfRangeException(nameof(deviceIndex));
        var provider = CanRegistry.Registry.Resolve(deviceType);
        var (devOpt, devCfg) = provider.GetDeviceOptions();
        ((ControlCanDeviceInitOptionsConfigurator)devCfg).DeviceIndex((uint)deviceIndex);
        var (device, lease) = ControlCanDeviceMultiplexer.Acquire(deviceType, (uint)deviceIndex, () =>
        {
            var d = provider.Factory.CreateDevice(devOpt);
            if (d == null)
                throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"Factory '{provider.Factory.GetType().FullName}' returned null device.");
            return d;
        });
        var (chOpt, chCfg) = provider.GetChannelOptions();
        configure?.Invoke((ControlCanBusInitConfigurator)chCfg);
        chCfg.UseChannelIndex(channelIndex);

        var transceiver = provider.Factory.CreateTransceivers(device.Options, chCfg);
        if (transceiver == null)
            throw new CanFactoryException(CanKitErrorCode.TransceiverMismatch, $"Factory '{provider.Factory.GetType().FullName}' returned null transceiver.");

        var channel = provider.Factory.CreateBus(device, chOpt, transceiver, provider);
        if (channel == null)
            throw new CanBusCreationException($"Factory '{provider.Factory.GetType().FullName}' returned null channel.");

        if (channel is IBusOwnership own)
            own.AttachOwner(lease);
        return (ControlCanBus)channel;
    }
}

