using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Abstractions.API.Common;

/// <summary>
/// Represents a single CAN filter rule that can be compiled to a predicate.
/// 表示可编译为谓词函数的单条 CAN 过滤规则。
/// </summary>
public interface IFilterRule
{
    /// <summary>
    /// Builds a predicate used to test whether a frame matches this rule.
    /// 构建用于判断帧是否匹配该规则的谓词函数。
    /// </summary>
    /// <returns>Predicate that returns true if the frame matches.（当帧匹配规则时返回 true 的谓词）</returns>
    Func<CanFrame, bool> Build();

    /// <summary>
    /// Indicates which ID space this rule targets (standard or extended).
    /// 指示该规则作用的 ID 空间（标准或扩展）。
    /// </summary>
    CanFilterIDType FilterIdType { get; }
}
