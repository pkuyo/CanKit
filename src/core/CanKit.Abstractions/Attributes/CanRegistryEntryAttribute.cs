using System;

namespace CanKit.Abstractions.Attributes;

/// <summary>
/// Legacy registration metadata. This attribute is not scanned and does not register components.
/// 旧版注册元数据。此特性不再被扫描，也不会注册组件。
/// </summary>
[Obsolete("CanRegistryEntryAttribute is no longer scanned. Use CanKitRegistration.Register() for static registration.")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class CanRegistryEntryAttribute : Attribute
{
    public CanRegistryEntryAttribute(CanRegistryEntryKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    /// <summary>
    /// Entry kind, used for grouping or ordering between categories.
    /// 条目类别（如 Adapter/Transport/Protocol）。
    /// </summary>
    public CanRegistryEntryKind Kind { get; }

    /// <summary>
    /// Optional display/name for this entry. 可选名称。
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Execution order among entries. Smaller first. 执行顺序，越小越先。
    /// </summary>
    public int Order { get; init; } = 0;

    /// <summary>
    /// Whether this entry is enabled. 是否启用。
    /// </summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Registry entry categories. 注册入口类别。
/// </summary>
public enum CanRegistryEntryKind
{
    Adapter = 0,
    Transport = 1,
    Protocol = 2,
    Misc = 3,
}
