using System.Collections.Generic;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Impl.Options
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

        public DeviceType DeviceType { get; set; }
        public uint TxTimeOut { get; set; }
        public bool MergeReceive { get; set; }
    }
}