using CanKit.Adapter.SocketCAN.Definitions;
using CanKit.Adapter.SocketCAN.Transceivers;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.SocketCAN;

[CanFactory("SocketCAN")]
public sealed class SocketCanFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        // SocketCAN has no real device; use generic NullDevice to keep typing for options.
        return new NullDevice<NullDeviceOptions>(options);
    }

    public ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver)
    {
        return new SocketCanBus(options, transceiver);
    }

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IBusInitOptionsConfigurator busOptions)
    {
        // SocketCAN supports classic + FD; no merged receive abstraction.
        return busOptions.ProtocolMode switch
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
