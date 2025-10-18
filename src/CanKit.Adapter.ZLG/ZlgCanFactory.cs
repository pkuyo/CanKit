using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Options;
using CanKit.Adapter.ZLG.Providers;
using CanKit.Adapter.ZLG.Transceivers;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.ZLG;

[CanFactory("Zlg")]
public sealed class ZlgCanFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        return new ZlgCanDevice(options);
    }

    public ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver)
    {

        if (device is not ZlgCanDevice zlgCanDevice)
            throw new CanFactoryDeviceMismatchException(typeof(ZlgCanDevice), device?.GetType() ?? typeof(ICanDevice));

        return new ZlgCanBus(zlgCanDevice, options, transceiver);
    }

    public bool Support(DeviceType deviceType)
    {
        return deviceType is ZlgDeviceType;
    }

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator configurator,
        IBusInitOptionsConfigurator busOptions)
    {
        if (configurator.Provider is not ZlgCanProvider provider)
            throw new CanProviderMismatchException(typeof(ZlgCanProvider), configurator.Provider?.GetType() ?? typeof(ICanModelProvider));

        if (configurator.Provider is PCIECANFDProvider or PCIECANFD200UProvider)
        {
            ((ZlgBusInitConfigurator)busOptions).MergeReceive(true);
            return new ZlgCanMergeTransceiver(); //Only support merge receive
        }

        if (((ZlgBusInitConfigurator)busOptions).EnableMergeReceive)
            return new ZlgCanMergeTransceiver();

        if (busOptions.ProtocolMode == CanProtocolMode.Can20
            && (uint)(configurator.Features & CanFeature.CanClassic) != 0U)
            return new ZlgCanClassicTransceiver();

        if (busOptions.ProtocolMode == CanProtocolMode.CanFd
            && (uint)(configurator.Features & CanFeature.CanFd) != 0U)
            return new ZlgCanFdTransceiver();

        var requiredFeature = busOptions.ProtocolMode switch
        {
            CanProtocolMode.Can20 => CanFeature.CanClassic,
            CanProtocolMode.CanFd => CanFeature.CanFd,
            _ => default
        };

        if (requiredFeature == 0)
        {
            throw new CanFactoryException(
                CanKitErrorCode.FeatureNotSupported,
                $"Protocol mode '{busOptions.ProtocolMode}' is not supported by provider '{provider.DeviceType.Id}'.");
        }

        throw new CanFeatureNotSupportedException(requiredFeature, configurator.Features);
    }
}
