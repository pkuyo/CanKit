using System;

namespace Pkuyo.CanKit.Net.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class CanEndPointAttribute(string scheme) : Attribute
{
    public string Scheme { get; } = scheme;
}
