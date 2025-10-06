

using System;

namespace CanKit.Core.Attributes;

public sealed class CanFactoryAttribute(string factoryId) : Attribute
{
    public string FactoryId => factoryId;
}
