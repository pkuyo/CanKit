using System.Collections.Generic;
using ZlgCAN.Net.Core.Abstractions;

namespace ZlgCAN.Net.Core.Impl.Options
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

        public bool CanFdStandard { get; set; }
        public uint ABitBaudRate { get; set; }
        public uint DBitBaudRate { get; set; }
        public uint BaudRate { get; set; }
        public int ChannelIndex { get; set; }
        public bool InternalResistance { get; set; }
        public FilterMode FilterMode { get; set; }
        public uint FilterStart { get; set; }
        public uint FilterEnd { get; set; }
        public bool FilterEnable { get; set; }
        public uint AccCode { get; set; }
        public uint AccMask { get; set; }
    }
}