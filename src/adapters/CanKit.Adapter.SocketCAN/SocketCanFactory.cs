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

    public ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver,
        ICanModelProvider provider)
    {
        return new SocketCanBus(options, transceiver, provider);
    }

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IBusInitOptionsConfigurator busOptions)
    {
        // SocketCAN supports classic + FD;
        return busOptions.ProtocolMode switch
        {
            CanProtocolMode.CanFd => new SocketCanFdTransceiver(),
            _ => new SocketCanClassicTransceiver(),
        };
    }

    public bool Support(DeviceType deviceType) => deviceType.Equals(LinuxDeviceType.SocketCAN);
}
