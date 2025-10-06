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

    public ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver)
    {
        return new KvaserBus(options, transceiver);
    }

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IBusInitOptionsConfigurator busOptions)
    {
        return busOptions.ProtocolMode switch
        {
            CanProtocolMode.Can20 => new Transceivers.KvaserClassicTransceiver(),
            CanProtocolMode.CanFd => new Transceivers.KvaserFdTransceiver(),
            _ => throw new Exception() //TODO:返回更适合的错误类型
        };
    }

    public bool Support(DeviceType deviceType) => deviceType.Equals(KvaserDeviceType.CANlib);
}

