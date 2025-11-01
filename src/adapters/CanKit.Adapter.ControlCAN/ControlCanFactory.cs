using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.ControlCAN.Options;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.ControlCAN;

[CanFactory("ControlCAN")]
public sealed class ControlCanFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options) => new ControlCanDevice(options);

    public ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
    {
        if (device is not ControlCanDevice ctlDev)
            throw new CanFactoryDeviceMismatchException(typeof(ControlCanDevice), device?.GetType() ?? typeof(ICanDevice));
        return new ControlCanBus(ctlDev, options, transceiver, provider);
    }

    public bool Support(DeviceType deviceType) => deviceType is Definitions.ControlCanDeviceType;

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator configurator, IBusInitOptionsConfigurator busOptions)
    {
        // Only classic CAN supported via ControlCAN
        if (busOptions.ProtocolMode == CanProtocolMode.Can20)
            return new ControlCanTransceiver();
        throw new CanFeatureNotSupportedException(CanFeature.CanFd, configurator.Features);
    }
}
