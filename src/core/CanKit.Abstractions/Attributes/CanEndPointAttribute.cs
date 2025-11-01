using System;

namespace CanKit.Abstractions.Attributes;

/// <summary>
/// Marks a CAN endpoint class and declares its URI scheme and aliases.
/// 标记一个 CAN 端点类型，并声明其 URI 方案与别名。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CanEndPointAttribute(string scheme, string[] alias) : Attribute
{
    /// <summary>
    /// URI scheme used to identify this endpoint type. (用于标识该端点类型的 URI 方案)
    /// </summary>
    public string Scheme { get; } = scheme;

    /// <summary>
    /// Alias list for the endpoint scheme. (该端点方案的别名列表)
    /// </summary>
    public string[] Alias { get; } = alias;
}
