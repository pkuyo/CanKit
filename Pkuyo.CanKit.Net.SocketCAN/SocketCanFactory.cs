using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.SocketCAN.Definitions;

namespace Pkuyo.CanKit.SocketCAN;

[CanFactory("SocketCAN")]
public sealed class SocketCanFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        return new SocketCanDevice(options);
    }

    public ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, ITransceiver transceiver)
    {
        if (device is not SocketCanDevice scDevice)
            throw new CanFactoryDeviceMismatchException(typeof(SocketCanDevice), device?.GetType() ?? typeof(ICanDevice));
        return new SocketCanChannel(scDevice, options, transceiver);
    }

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IChannelInitOptionsConfigurator channelOptions)
    {
        // SocketCAN supports classic + FD; no merged receive abstraction.
        return channelOptions.ProtocolMode switch
        {
            CanProtocolMode.Can20 => new SocketCanClassicTransceiver(),
            CanProtocolMode.CanFd => new SocketCanFdTransceiver(),
            _ => throw new CanFeatureNotSupportedException(CanFeature.MergeReceive, CanFeature.CanClassic | CanFeature.CanFd)
        };
    }

    public bool Support(DeviceType deviceType)
    {
        return deviceType.Equals(LinuxDeviceType.SocketCAN);
    }
}

