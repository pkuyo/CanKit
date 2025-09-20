using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Definitions;

namespace Pkuyo.CanKit.ZLG.Providers;
public sealed class USBCANProvider(DeviceType deviceType) : ZlgCanProvider
{
    public override DeviceType DeviceType => deviceType;
    
    public override ZlgFeature ZlgFeature => ZlgFeature.MaskFilter;
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