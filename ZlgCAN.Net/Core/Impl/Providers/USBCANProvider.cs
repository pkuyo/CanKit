using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Impl.Providers
{

    public class USBCANIIProvider : ZlgCanProvider
    {
        public override DeviceType DeviceType => ZlgDeviceType.ZCAN_USBCAN2;
        public override CanFeature Features =>
            CanFeature.CanClassic | CanFeature.AccMask | CanFeature.BusUsage | CanFeature.AutoSend;
    }
    
    public class USBCANIProvider : ZlgCanProvider
    {
        public override DeviceType DeviceType => ZlgDeviceType.ZCAN_USBCAN1;
        public override CanFeature Features =>
            CanFeature.CanClassic | CanFeature.AccMask | CanFeature.BusUsage | CanFeature.AutoSend;
    }
}