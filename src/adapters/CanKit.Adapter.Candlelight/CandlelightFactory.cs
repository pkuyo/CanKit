using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.Candlelight;

public sealed class CandlelightFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options) => new CandlelightDevice((CandlelightDeviceOptions)options);

    public ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
    {
        if (device is not CandlelightDevice candleDevice)
            throw new CanFactoryDeviceMismatchException(typeof(CandlelightDevice), device?.GetType() ?? typeof(ICanDevice));
        return new CandlelightBus(candleDevice, (CandlelightBusOptions)options, transceiver);
    }

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator configurator, IBusInitOptionsConfigurator busOptions)
    {
        if (busOptions.ProtocolMode != CanProtocolMode.Can20)
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, configurator.Features);
        return new CandlelightTransceiver();
    }

    public bool Support(DeviceType deviceType) => deviceType == CandlelightDeviceType.Candlelight;
}
