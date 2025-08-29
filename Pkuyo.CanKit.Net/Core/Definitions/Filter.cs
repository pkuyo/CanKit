using System.Collections.Generic;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    public abstract record FilterRule(FilterMode IdType)
    {
        public sealed record Single(uint Id, FilterMode Type) : FilterRule(Type);
        public sealed record Range(uint From, uint To, FilterMode Type) : FilterRule(Type);
        public sealed record Mask(uint AccCode, uint AccMask, FilterMode Type) : FilterRule(Type);
        public sealed record Set(IReadOnlyList<uint> Ids, FilterMode Type) : FilterRule(Type);
    }
    
    
}