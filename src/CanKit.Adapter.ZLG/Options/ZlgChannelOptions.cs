using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Options
{
    public sealed class ZlgBusOptions(ICanModelProvider provider) : IBusOptions
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
        public bool BusUsageEnabled { get; set; }
        public uint BusUsagePeriodTime { get; set; } = 200;
        public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.AlwaysRetry;
    }
}
