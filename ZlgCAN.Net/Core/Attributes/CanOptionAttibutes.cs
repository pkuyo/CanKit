using System;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Attributes
{

    public sealed class CanModelAttribute(string deviceType) : Attribute
    {
        public string DeviceType { get; } = deviceType;
    }

    
  
}