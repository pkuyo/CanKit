using System;

namespace Pkuyo.CanKit.Net.Core.Attributes
{

    public sealed class CanModelAttribute(string deviceType) : Attribute
    {
        public string DeviceType { get; } = deviceType;
    }

    
  
}