using System.Collections.Generic;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Providers;

public class PCIECANFD200UProvider : ZlgCanProvider
{
    public override DeviceType DeviceType => ZlgDeviceType.ZCAN_PCIE_CANFD_200U;

    public override CanFeature StaticFeatures => base.StaticFeatures |
                                                 CanFeature.CyclicTx |
                                                 CanFeature.Echo |
                                                 CanFeature.CanFd;

    public override ZlgFeature ZlgFeature => ZlgFeature.RangeFilter;
}

public class PCIECANFDProvider(DeviceType deviceType) : ZlgCanProvider
{
    public override DeviceType DeviceType => deviceType;

    public override CanFeature StaticFeatures => base.StaticFeatures |
                                                 CanFeature.ListenOnly |
                                                 CanFeature.Echo |
                                                 CanFeature.CyclicTx |
                                                 CanFeature.CanFd;

    public override ZlgFeature ZlgFeature => ZlgFeature.RangeFilter;
}



public sealed class PCIECANFDProviderGroup : ICanModelProviderGroup
{
    public IEnumerable<DeviceType> SupportedDeviceTypes =>
    [
        ZlgDeviceType.ZCAN_PCIE_CANFD_400U,
        ZlgDeviceType.ZCAN_PCIE_CANFD_200U_EX,
        ZlgDeviceType.ZCAN_PCIE_CANFD_200U_M2,
        ZlgDeviceType.ZCAN_MINI_PCIE_CANFD,
    ];

    public ICanModelProvider Create(DeviceType deviceType)
    {
        return new PCIECANFDProvider(deviceType);
    }
}

