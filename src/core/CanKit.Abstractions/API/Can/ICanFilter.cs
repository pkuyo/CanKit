using System.Collections.Generic;
using CanKit.Abstractions.API.Common;

namespace CanKit.Abstractions.API.Can
{
    /// <summary>
    /// Read-only view of CAN filter rules. 用于对外暴露 CAN 过滤规则的只读接口。
    /// </summary>
    public interface ICanFilter
    {
        IList<IFilterRule> FilterRules { get; }
        IList<IFilterRule> SoftwareFilterRules { get; }
    }
}

