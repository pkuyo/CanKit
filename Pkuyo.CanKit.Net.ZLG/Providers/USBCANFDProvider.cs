

using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG;
using Pkuyo.CanKit.ZLG.Definitions;

public class USBCANFD100UProvider : ZlgCanProvider
{
    public override DeviceType DeviceType => ZlgDeviceType.ZCAN_USBCANFD_100U;
    
    public override CanFeature Features => base.Features | CanFeature.BusUsage | CanFeature.CyclicTx | 
                                           CanFeature.CanFd | CanFeature.MergeReceive;
    
    public override ZlgFeature ZlgFeature => ZlgFeature.RangeFilter;
}

public class USBCANFD200UProvider : ZlgCanProvider
{
    public override DeviceType DeviceType => ZlgDeviceType.ZCAN_USBCANFD_200U;
    
    public override CanFeature Features => base.Features | CanFeature.BusUsage | CanFeature.CyclicTx | 
                                           CanFeature.CanFd | CanFeature.MergeReceive;
    
    public override ZlgFeature ZlgFeature => ZlgFeature.RangeFilter;
}

public class USBCANFD400UProvider : ZlgCanProvider
{
    public override DeviceType DeviceType => ZlgDeviceType.ZCAN_USBCANFD_400U;
    
    public override CanFeature Features => base.Features | CanFeature.BusUsage | CanFeature.CyclicTx | 
                                           CanFeature.CanFd | CanFeature.MergeReceive;
    
    public override ZlgFeature ZlgFeature => ZlgFeature.RangeFilter;
}

public class USBCANFD800UProvider : ZlgCanProvider
{
    public override DeviceType DeviceType => ZlgDeviceType.ZCAN_USBCANFD_800U;
    
    public override CanFeature Features => base.Features | CanFeature.BusUsage | CanFeature.CyclicTx | 
                                           CanFeature.CanFd | CanFeature.MergeReceive;
    
    public override ZlgFeature ZlgFeature => ZlgFeature.RangeFilter;
}

public class USBCANFDMINIProvider : ZlgCanProvider
{
    public override DeviceType DeviceType => ZlgDeviceType.ZCAN_USBCANFD_MINI;
    
    public override CanFeature Features => base.Features | CanFeature.BusUsage | CanFeature.CyclicTx | 
                                           CanFeature.CanFd | CanFeature.MergeReceive;
    
    public override ZlgFeature ZlgFeature => ZlgFeature.RangeFilter;
}