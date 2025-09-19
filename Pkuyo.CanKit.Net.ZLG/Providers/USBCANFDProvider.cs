using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Definitions;

namespace Pkuyo.CanKit.ZLG.Providers;

public class USBCANFDProvider(DeviceType deviceType) : ZlgCanProvider
{
    public override DeviceType DeviceType => deviceType;
    
    public override CanFeature Features => base.Features | CanFeature.BusUsage | CanFeature.CyclicTx | 
                                           CanFeature.CanFd | CanFeature.MergeReceive;
    
    public override ZlgFeature ZlgFeature => ZlgFeature.RangeFilter;
}

public class USBCANFDProviderGroup : ICanModelProviderGroup
{
    public IEnumerable<DeviceType> SupportedDeviceTypes =>
    [
        ZlgDeviceType.ZCAN_USBCANFD_100U,
        ZlgDeviceType.ZCAN_USBCANFD_200U,
        ZlgDeviceType.ZCAN_USBCANFD_400U,
        ZlgDeviceType.ZCAN_USBCANFD_800U,
        ZlgDeviceType.ZCAN_USBCANFD_MINI
    ];
    
    public ICanModelProvider Create(DeviceType deviceType)
    {
        return new USBCANFDProvider(deviceType);
    }
}