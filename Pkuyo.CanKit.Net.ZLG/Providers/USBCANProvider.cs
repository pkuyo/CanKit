using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Definitions;

namespace Pkuyo.CanKit.ZLG.Providers
{

    public class USBCANIIProvider : ZlgCanProvider
    {
        public override DeviceType DeviceType => ZlgDeviceType.ZCAN_USBCAN2;
        
        public override ZlgFeature ZlgFeature => ZlgFeature.MaskFilter;
    }
    
    public class USBCANIProvider : ZlgCanProvider
    {
        public override DeviceType DeviceType => ZlgDeviceType.ZCAN_USBCAN1;

        public override ZlgFeature ZlgFeature => ZlgFeature.MaskFilter;
    }
    
    public class PCI9820IProvider : ZlgCanProvider
    {
        public override DeviceType DeviceType => ZlgDeviceType.ZCAN_PCI9820I;
        
        public override ZlgFeature ZlgFeature => ZlgFeature.MaskFilter;
    }
}