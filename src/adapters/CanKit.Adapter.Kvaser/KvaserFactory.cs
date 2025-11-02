using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.Kvaser.Definitions;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.Kvaser;

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

