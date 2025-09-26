using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Options
{
    [CanOption]
    public sealed partial class ZlgDeviceOptions(ICanModelProvider provider) : IDeviceOptions
    {
        public ICanModelProvider Provider => provider;

        public DeviceType DeviceType => provider.DeviceType;

        public partial void Apply(ICanApplier applier, bool force = false);

        [CanOptionItem("device_index", CanOptionType.Init, "0U")]
        public partial uint DeviceIndex { get; set; }

        [CanOptionItem("/tx_timeout", CanOptionType.Init, "100U")]
        public partial uint TxTimeOut { get; set; }

        [CanOptionItem("/set_device_recv_merge", CanOptionType.Init, "true")]
        public partial bool MergeReceive { get; set; }
    }
}
