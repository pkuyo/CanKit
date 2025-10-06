using System;

namespace CanKit.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class CanEndPointAttribute(string scheme) : Attribute
{
    public string Scheme { get; } = scheme;
}
