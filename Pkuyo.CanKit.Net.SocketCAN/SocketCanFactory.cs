using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.SocketCAN.Transceivers;
using Pkuyo.CanKit.SocketCAN.Definitions;

namespace Pkuyo.CanKit.Net.SocketCAN;

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
