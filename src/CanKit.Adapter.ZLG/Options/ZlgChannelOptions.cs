using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Options
{
    [CanOption]
    public sealed partial class ZlgBusOptions(ICanModelProvider provider) : IBusOptions
    {
        public enum MaskFilterType : byte
        {
            Single = 0,
            Double = 1
        }

        public bool SoftwareFilterEnabled { get; set; }

        public int PollingInterval { get; set; } = 20;

        public MaskFilterType FilterType { get; set; }

        public ICanModelProvider Provider => provider;

        public partial void Apply(ICanApplier applier, bool force = false);

        public int ChannelIndex { get; set; }
        public string? ChannelName { get; set; }

        public CanBusTiming BitTiming { get; set; }

        public ChannelWorkMode WorkMode { get; set; }

        public CanProtocolMode ProtocolMode { get; set; }

        public CanFilter Filter { get; set; } = new();
        public CanFeature EnabledSoftwareFallback { get; set; }
        public bool AllowErrorInfo { get; set; }

        public bool InternalResistance { get; set; }
        public int AsyncBufferCapacity { get; set; } = 0;
        public int ReceiveLoopStopDelayMs { get; set; } = 200;

        [CanOptionItem("/set_bus_usage_enable", CanOptionType.Init, "false")]
        public bool BusUsageEnabled
        {
            get => Get_BusUsageEnabled();
            set => Set_BusUsageEnabled(value);
        }

        [CanOptionItem("/set_bus_usage_period", CanOptionType.Init, "200U")]
        public uint BusUsagePeriodTime
        {
            get => Get_BusUsagePeriodTime();
            set => Set_BusUsagePeriodTime(value);
        }

        [CanOptionItem("/set_tx_retry_policy", CanOptionType.Init,
            "CanKit.Core.Definitions.TxRetryPolicy.NoRetry")]
        public TxRetryPolicy TxRetryPolicy
        {
            get => Get_TxRetryPolicy();
            set => Set_TxRetryPolicy(value);
        }
    }
}
