using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.ZLG.Transceivers;

namespace Pkuyo.CanKit.ZLG;

[CanFactory("Zlg")]
public class ZlgCanFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        return new ZlgCanDevice(options);
    }

    public ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, ITransceiver transceiver)
    {
        
        if (device is not ZlgCanDevice zlgCanDevice)
            throw new CanFactoryDeviceMismatchException(typeof(ZlgCanDevice), device?.GetType() ?? typeof(ICanDevice));

        return new ZlgCanChannel(zlgCanDevice, options, transceiver);
    }

    public bool Support(DeviceType deviceType)
    {
        return deviceType.Id.StartsWith("ZLG.");
    }
    
    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator configurator,
        IChannelInitOptionsConfigurator channelOptions)
    {
        if (configurator.Provider is not ZlgCanProvider provider)
            throw new CanProviderMismatchException(typeof(ZlgCanProvider), configurator.Provider?.GetType() ?? typeof(ICanModelProvider));

        if (channelOptions.ProtocolMode == CanProtocolMode.Merged && 
            (provider.Features & CanFeature.MergeReceive) != 0U)
            return new ZlgMergeTransceiver();

        if (channelOptions.ProtocolMode == CanProtocolMode.Can20
            && (uint)(provider.Features & CanFeature.CanClassic) != 0U)
            return new ZlgCanClassicTransceiver();

        if (channelOptions.ProtocolMode == CanProtocolMode.CanFd
            && (uint)(provider.Features & CanFeature.CanFd) != 0U)
            return new ZlgCanFdTransceiver();

        var requiredFeature = channelOptions.ProtocolMode switch
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
                $"Protocol mode '{channelOptions.ProtocolMode}' is not supported by provider '{provider.DeviceType.Id}'.");
        }

        throw new CanFeatureNotSupportedException(requiredFeature, provider.Features);
    }

}