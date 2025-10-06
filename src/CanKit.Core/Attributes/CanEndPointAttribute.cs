using System;

namespace CanKit.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class CanEndPointAttribute(string scheme, string[] alias) : Attribute
{
    public string Scheme { get; } = scheme;

    public string[] Alias { get; } = alias;
}
