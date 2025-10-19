using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Options
{
    public sealed class ZlgDeviceOptions(ICanModelProvider provider) : IDeviceOptions
    {

        public uint DeviceIndex { get; set; }

        public DeviceType DeviceType { get; } = provider.DeviceType;
        public CanFeature Features { get; set; } = provider.StaticFeatures;
    }
}
