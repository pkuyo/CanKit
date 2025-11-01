

using System;

namespace CanKit.Abstractions.Attributes;

/// <summary>
/// Marks a CAN factory class with a unique identifier.
/// 使用唯一标识标记一个 CAN 工厂类型。
/// </summary>
public sealed class CanFactoryAttribute(string factoryId) : Attribute
{
    /// <summary>
    /// Unique factory identifier. (工厂的唯一标识)
    /// </summary>
    public string FactoryId => factoryId;
}
