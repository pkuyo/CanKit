using System.Collections.Generic;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Providers;

public class CANDTUProvider(DeviceType deviceType) : ZlgCanProvider
{
    public override DeviceType DeviceType => deviceType;

    public override CanFeature StaticFeatures => base.StaticFeatures |
                                                 CanFeature.ListenOnly |
                                                 CanFeature.MaskFilter;

}

public sealed class CANDTUProviderGroup : ICanModelProviderGroup
{
    public IEnumerable<DeviceType> SupportedDeviceTypes =>
    [
        ZlgDeviceType.ZCAN_CANDTU_100UR,
        ZlgDeviceType.ZCAN_CANDTU_200UR
    ];

    public ICanModelProvider Create(DeviceType deviceType)
    {
        return new CANDTUProvider(deviceType);
    }
}

