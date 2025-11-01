

using System;

namespace CanKit.Abstractions.Attributes;

public sealed class CanFactoryAttribute(string factoryId) : Attribute
{
    public string FactoryId => factoryId;
}
