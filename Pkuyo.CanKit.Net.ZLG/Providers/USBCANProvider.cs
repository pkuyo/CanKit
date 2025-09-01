using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.ZLG.Providers
{

    public class USBCANIIProvider : ZlgCanProvider
    {
        public override DeviceType DeviceType => ZlgDeviceType.ZCAN_USBCAN2;
        public override CanFeature Features=> base.Features | CanFeature.BusUsage | CanFeature.CyclicTx;
    }
    
    public class USBCANIProvider : ZlgCanProvider
    {
        public override DeviceType DeviceType => ZlgDeviceType.ZCAN_USBCAN1;
        public override CanFeature Features => base.Features | CanFeature.BusUsage | CanFeature.CyclicTx;
    }
    
    public class PCI9820IProvider : ZlgCanProvider
    {
        public override DeviceType DeviceType => ZlgDeviceType.ZCAN_PCI9820I;
        public override CanFeature Features => base.Features | CanFeature.BusUsage | CanFeature.CyclicTx;
    }
}