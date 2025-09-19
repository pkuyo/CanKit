using System;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Attributes
{
    
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class CanOptionAttribute : Attribute
    {
        
    }
    
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class CanOptionItemAttribute(string optionName, CanOptionType type, string? defaultValue = null) : Attribute
    {
        public string OptionName { get; } = optionName;
        public CanOptionType Type { get; } = type;
        public string? DefaultValue { get; } = defaultValue;
    }
}