using System.Collections.Generic;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Providers;



//USBCANFD不支持读取错误段信息
public sealed class USBCANFDProvider(DeviceType deviceType) : ZlgCanProvider
{
    public override DeviceType DeviceType => deviceType;

    public override CanFeature StaticFeatures => base.StaticFeatures | CanFeature.BusUsage | CanFeature.CyclicTx |
                                                 CanFeature.CanFd;

    public override ZlgFeature ZlgFeature => ZlgFeature.RangeFilter;
}

public sealed class USBCANFDProviderGroup : ICanModelProviderGroup
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
