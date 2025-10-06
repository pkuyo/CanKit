using CanKit.Adapter.PCAN.Definitions;
using CanKit.Adapter.PCAN.Transceivers;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.PCAN;

[CanFactory("PCAN")]
public sealed class PcanFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        return new NullDevice<NullDeviceOptions>(options);
    }

    public ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver)
    {
        return new PcanBus(options, transceiver);
    }

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IBusInitOptionsConfigurator busOptions)
    {
        return busOptions.ProtocolMode switch
        {
            CanProtocolMode.Can20 => new PcanClassicTransceiver(),
            CanProtocolMode.CanFd => new PcanFdTransceiver(),
            _ => throw new CanFeatureNotSupportedException(CanFeature.CanFd, CanFeature.CanClassic)
        };
    }

    public bool Support(DeviceType deviceType) => deviceType.Equals(PcanDeviceType.PCANBasic);
}
