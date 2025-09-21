using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.ZLG.Transceivers;

namespace Pkuyo.CanKit.ZLG;

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

        if (busOptions.ProtocolMode == CanProtocolMode.Merged && 
            (configurator.Features & CanFeature.MergeReceive) != 0U)
            return new ZlgMergeTransceiver();

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
            CanProtocolMode.Merged => CanFeature.MergeReceive,
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