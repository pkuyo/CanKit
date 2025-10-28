using System.Collections.Generic;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Providers;

public class USBCANEProvider(DeviceType deviceType) : ZlgCanProvider
{
    public override DeviceType DeviceType => deviceType;

    public override CanFeature StaticFeatures => base.StaticFeatures |
                                                 CanFeature.CyclicTx |
                                                 CanFeature.RangeFilter;

}

public sealed class USBCANEProviderGroup : ICanModelProviderGroup
{
    public IEnumerable<DeviceType> SupportedDeviceTypes =>
    [
        ZlgDeviceType.ZCAN_PCI5010U,
        ZlgDeviceType.ZCAN_PCI5020U,
        ZlgDeviceType.ZCAN_USBCAN_E_U,
        ZlgDeviceType.ZCAN_USBCAN_2E_U,
        ZlgDeviceType.ZCAN_USBCAN_4E_U,
        ZlgDeviceType.ZCAN_USBCAN_8E_U
    ];

    public ICanModelProvider Create(DeviceType deviceType)
    {
        return new USBCANEProvider(deviceType);
    }
}
