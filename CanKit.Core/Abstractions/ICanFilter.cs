using System.Collections.Generic;
using CanKit.Core.Definitions;

namespace CanKit.Core.Abstractions
{
    /// <summary>
    /// Read-only view of CAN filter rules. 用于对外暴露 CAN 过滤规则的只读接口。
    /// </summary>
    public interface ICanFilter
    {
        IReadOnlyList<FilterRule> FilterRules { get; }
        IReadOnlyList<FilterRule> SoftwareFilterRules { get; }
    }
}

