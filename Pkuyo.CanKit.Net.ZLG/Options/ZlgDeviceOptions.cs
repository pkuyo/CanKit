using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.ZLG.Options
{
    public class ZlgDeviceOptions (ICanModelProvider provider): IDeviceOptions
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

        public DeviceType DeviceType => provider.DeviceType;

        public uint DeviceIndex { get; set; } 
        
        public uint TxTimeOut { get; set; }
        public bool MergeReceive { get; set; }
    }
}