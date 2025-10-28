using System.Collections.Generic;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Options
{
    public sealed class ZlgBusOptions(ICanModelProvider provider) : IBusOptions
    {

        /// <summary>
        /// merge receive, one device only call one times (合并接收，一个设备只需要启用一次)
        /// </summary>
        public bool? MergeReceive { get; set; }

        /// <summary>
        /// Polling interval in ms (轮询间隔，毫秒)。
        /// </summary>
        public int PollingInterval { get; set; } = 20;
        public int ChannelIndex { get; set; }
        public string? ChannelName { get; set; }

        public CanBusTiming BitTiming { get; set; }

        public ChannelWorkMode WorkMode { get; set; }

        public CanProtocolMode ProtocolMode { get; set; }

        public CanFilter Filter { get; set; } = new();
        public CanFeature EnabledSoftwareFallback { get; set; }
        public CanFeature Features { get; set; } = provider.StaticFeatures;
        public Capability Capabilities { get; set; } = new(provider.StaticFeatures,
            new Dictionary<string, object?> { { "zlg_features", ((ZlgCanProvider)provider).ZlgFeature } });
        public bool AllowErrorInfo { get; set; }

        public bool InternalResistance { get; set; }
        public int AsyncBufferCapacity { get; set; } = 0;
        public int ReceiveLoopStopDelayMs { get; set; } = 200;
        public bool BusUsageEnabled { get; set; }
        public uint BusUsagePeriodTime { get; set; } = 200;
        public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.AlwaysRetry;
        public ZlgFeature ZlgFeatures { get; } = ((ZlgCanProvider)provider).ZlgFeature;
    }
}
