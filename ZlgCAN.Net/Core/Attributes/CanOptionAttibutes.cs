using System;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Attributes
{

    public sealed class CanModelAttribute(string deviceType) : Attribute
    {
        public string DeviceType { get; } = deviceType;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class CanValueAttribute(string name, Type type) : Attribute
    {
        public string Name { get; } = name;
        public Type Type { get; } = type;
        public CanValueAccess Access { get; init; } = CanValueAccess.GetSet;
        
        public object DefaultValue { get; init; }
    }

    
    public sealed class CanOptions : Attribute
    {
        
    }
}