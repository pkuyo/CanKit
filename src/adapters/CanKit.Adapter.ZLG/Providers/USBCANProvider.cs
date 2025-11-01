using System.Collections.Generic;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Providers;

public sealed class USBCANProvider(DeviceType deviceType) : ZlgCanProvider
{
    public override DeviceType DeviceType => deviceType;

    public override CanFeature StaticFeatures => base.StaticFeatures | CanFeature.MaskFilter;
}

public sealed class USBCANProviderGroup : ICanModelProviderGroup
{
    public IEnumerable<DeviceType> SupportedDeviceTypes =>
    [
        ZlgDeviceType.ZCAN_USBCAN1,
        ZlgDeviceType.ZCAN_USBCAN2,
        ZlgDeviceType.ZCAN_PCI9820,
        ZlgDeviceType.ZCAN_PCI9820I
    ];

    public ICanModelProvider Create(DeviceType deviceType)
    {
        return new USBCANProvider(deviceType);
    }
}
