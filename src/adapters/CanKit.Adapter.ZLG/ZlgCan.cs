using System;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Options;
using CanKit.Core.Exceptions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ZLG;

public static class ZlgCan
{
    public static ZlgCanBus Open(ZlgDeviceType deviceType, int deviceIndex, int channelIndex,
        Action<ZlgBusInitConfigurator>? configure = null)
    {
        if (channelIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(channelIndex));
        if (deviceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(deviceIndex));
        var provider = CanRegistry.Registry.Resolve(deviceType);
        var (devOpt, devCfg) = provider.GetDeviceOptions();
        ((ZlgDeviceInitOptionsConfigurator)devCfg).DeviceIndex((uint)deviceIndex);
        var (device, lease) = ZlgDeviceMultiplexer.Acquire(deviceType, (uint)deviceIndex, () =>
        {
            var d = provider.Factory.CreateDevice(devOpt);
            if (d == null)
                throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"Factory '{provider.Factory.GetType().FullName}' returned null device.");
            return d;
        });
        var (chOpt, chCfg) = provider.GetChannelOptions();
        configure?.Invoke((ZlgBusInitConfigurator)chCfg);
        chCfg.UseChannelIndex(channelIndex);

        var transceiver = provider.Factory.CreateTransceivers(device.Options, chCfg);
        if (transceiver == null)
            throw new CanFactoryException(CanKitErrorCode.TransceiverMismatch, $"Factory '{provider.Factory.GetType().FullName}' returned null transceiver.");

        var channel = provider.Factory.CreateBus(device, chOpt, transceiver, provider);
        if (channel == null)
            throw new CanBusCreationException($"Factory '{provider.Factory.GetType().FullName}' returned null channel.");

        if (channel is IBusOwnership own)
            own.AttachOwner(lease);
        return (ZlgCanBus)channel;
    }
}
