using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{
    

    public interface ICanApplier
    {
        bool ApplyOne<T>(string name, T value);
    }
    
    public interface ICanOptions
    {
        ICanModelProvider Provider { get; }

        bool HasChanges { get; }
        IEnumerable<string> GetChangedNames();
        void ClearChanges();

        void Apply(ICanApplier applier, bool force = false);
    }



    public interface IDeviceOptions : ICanOptions
    {
        DeviceType DeviceType { get; }

        uint TxTimeOut { get; set; }

        bool MergeReceive { get; set; }
    }

    public interface IChannelOptions : ICanOptions
    {
        
        int ChannelIndex { get; set; }
        
        BitTiming BitTiming { get; set; }
        
        bool InternalResistance { get; set; }
        
        bool BusUsageEnabled { get; set; }
        
        uint BusUsagePeriodTime { get; set; }
        
        ChannelWorkMode WorkMode { get; set; }
        
        TxRetryPolicy TxRetryPolicy { get; set; }
    }
}