

using System;

public sealed class CanFactoryAttribute(string factoryId) : Attribute
{
    public string FactoryId => factoryId;
}