using System.Collections.Generic;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Providers;

public class PCIECANFD200UProvider : ZlgCanProvider
{
    public override DeviceType DeviceType => ZlgDeviceType.ZCAN_PCIE_CANFD_200U;

    public override CanFeature StaticFeatures => base.StaticFeatures |
                                                 CanFeature.CyclicTx |
                                                 CanFeature.Echo |
                                                 CanFeature.CanFd |
                                                 CanFeature.RangeFilter;

    public override ZlgFeature ZlgFeature => ZlgFeature.MergeReceive;
}

public class PCIECANFDProvider(DeviceType deviceType) : ZlgCanProvider
{
    public override DeviceType DeviceType => deviceType;

    public override CanFeature StaticFeatures => base.StaticFeatures |
                                                 CanFeature.ListenOnly |
                                                 CanFeature.Echo |
                                                 CanFeature.CyclicTx |
                                                 CanFeature.CanFd |
                                                 CanFeature.RangeFilter;

    public override ZlgFeature ZlgFeature => ZlgFeature.MergeReceive;
}



public sealed class PCIECANFDProviderGroup : ICanModelProviderGroup
{
    public IEnumerable<DeviceType> SupportedDeviceTypes =>
    [
        ZlgDeviceType.ZCAN_PCIE_CANFD_400U,
        ZlgDeviceType.ZCAN_PCIE_CANFD_100U_EX,
        ZlgDeviceType.ZCAN_PCIE_CANFD_200U_EX,
        ZlgDeviceType.ZCAN_PCIE_CANFD_200U,
        ZlgDeviceType.ZCAN_MINI_PCIE_CANFD,
    ];

    public ICanModelProvider Create(DeviceType deviceType)
    {
        return new PCIECANFDProvider(deviceType);
    }
}

