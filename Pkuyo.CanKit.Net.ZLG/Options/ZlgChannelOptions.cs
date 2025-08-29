using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.ZLG.Options
{
    public class ZlgChannelOptions(ICanModelProvider provider) : IChannelOptions
    {
        public ICanModelProvider Provider => provider;
        public bool HasChanges { get; }

        public IEnumerable<string> GetChangedNames()
        {
            throw new System.NotImplementedException();
        }

        public void ClearChanges()
        {
            throw new System.NotImplementedException();
        }

        public void Apply(ICanApplier applier, bool force = false)
        {
            throw new System.NotImplementedException();
        }
        
        public int ChannelIndex { get; set; }
        public BitTiming BitTiming { get; set; }
        public bool InternalResistance { get; set; }
        public bool BusUsageEnabled { get; set; }
        public uint BusUsagePeriodTime { get; set; }
        public ChannelWorkMode WorkMode { get; set; }
        public TxRetryPolicy TxRetryPolicy { get; set; }
    }
}