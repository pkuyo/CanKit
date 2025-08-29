

using System;

namespace Pkuyo.CanKit.Net.Core.Attributes;

public sealed class CanFactoryAttribute(string factoryId) : Attribute
{
    public string FactoryId => factoryId;
}