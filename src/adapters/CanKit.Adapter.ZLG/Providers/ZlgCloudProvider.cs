using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.ZLG.Definitions;

namespace CanKit.Adapter.ZLG.Providers;

public class ZlgCloudProvider : ZlgCanProvider
{
    public override DeviceType DeviceType => ZlgDeviceType.ZCAN_CLOUD;

    public override ZlgFeature ZlgFeature => base.ZlgFeature | ZlgFeature.SkipBitRate;

    public override CanFeature StaticFeatures => base.StaticFeatures |
                                                 CanFeature.ListenOnly |
                                                 CanFeature.MaskFilter;
}
