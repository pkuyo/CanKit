using System.Collections.Generic;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    public abstract record FilterRule(CanFilterIDType IdIdType)
    {
        public sealed record Range(uint From, uint To) : FilterRule(CanFilterIDType.Standard)
        {
            public Range(uint From, uint To, CanFilterIDType idType) : this(From, To)
            {
                IdIdType = idType;
            }
        }

        public sealed record Mask(uint AccCode, uint AccMask) : FilterRule(CanFilterIDType.Standard)
        {
            public Mask(uint AccCode, uint AccMask, CanFilterIDType idType) : this(AccCode, AccMask)
            {
                IdIdType = idType;
            }
        }
    }

    public interface ICanFilter
    {
        IReadOnlyList<FilterRule> FilterRules { get; }
    }

    public class CanFilter : ICanFilter
    {
        public List<FilterRule> filterRules = new();

        public IReadOnlyList<FilterRule> FilterRules => filterRules;
    }
    
}