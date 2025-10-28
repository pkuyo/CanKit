using CanKit.Adapter.Kvaser.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.Kvaser;

[CanFactory("KVASER")]
public sealed class KvaserFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        return new NullDevice<NullDeviceOptions>(options);
    }

    public ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver,
        ICanModelProvider provider)
    {
        return new KvaserBus(options, transceiver, provider);
    }

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IBusInitOptionsConfigurator busOptions)
    {
        return busOptions.ProtocolMode switch
        {
            CanProtocolMode.CanFd => new Transceivers.KvaserFdTransceiver(),
            _ => new Transceivers.KvaserClassicTransceiver(),
        };
    }

    public bool Support(DeviceType deviceType) => deviceType.Equals(KvaserDeviceType.CANlib);
}

