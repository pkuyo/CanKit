using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.ZLG.Options
{
    [CanOption]
    public partial class ZlgChannelOptions(ICanModelProvider provider) : IChannelOptions
    {
        public ICanModelProvider Provider => provider;

        public partial void Apply(ICanApplier applier, bool force = false);

        [CanOptionItem("channel_index", CanOptionType.Init, "0")]
        public partial int ChannelIndex { get; set; }

        [CanOptionItem("bit_timing", CanOptionType.Init, 
            "new Pkuyo.CanKit.Net.Core.Definitions.BitTiming()")]
        public partial BitTiming BitTiming { get; set; }

        [CanOptionItem("/initenal_resistance", CanOptionType.Init, "true")]
        public partial bool InternalResistance { get; set; }

        [CanOptionItem("/set_bus_usage_enable", CanOptionType.Init, "false")]
        public partial bool BusUsageEnabled { get; set; }

        [CanOptionItem("/set_bus_usage_period", CanOptionType.Init, "200U")]
        public partial uint BusUsagePeriodTime { get; set; }

        [CanOptionItem("work_mode", CanOptionType.Init,
            "Pkuyo.CanKit.Net.Core.Definitions.ChannelWorkMode.Normal")]
        public ChannelWorkMode WorkMode { get; set; }

        [CanOptionItem("/set_tx_retry_policy", CanOptionType.Init,
            "Pkuyo.CanKit.Net.Core.Definitions.TxRetryPolicy.NoRetry")]
        public partial TxRetryPolicy TxRetryPolicy { get; set; }
    }
}