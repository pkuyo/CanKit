using System.Collections.Generic;

namespace CanKit.Abstractions.API.Common.Definitions;

/// <summary>
/// Capability report combining built-in CanFeature and optional custom feature bag.
/// ZH: 能力报告，包含内置的 CanFeature 与可选的自定义能力键值对。
/// </summary>
public sealed class Capability
{

    /// <summary>
    /// Built-in features mask.
    /// 内置的CanFeature能力集合。
    /// </summary>
    public CanFeature Features { get; }

    /// <summary>
    /// Provider-specific custom feature bag.
    /// 供应商自定义能力键值对。
    /// </summary>
    public IReadOnlyDictionary<string, object?> Custom { get; }

    public Capability(CanFeature features,
        IReadOnlyDictionary<string, object?>? custom = null)
    {
        Features = features;
        Custom = custom ?? new Dictionary<string, object?>();
    }
}

