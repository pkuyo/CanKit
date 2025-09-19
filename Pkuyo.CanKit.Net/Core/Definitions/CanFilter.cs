using System.Collections.Generic;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    /// <summary>
    /// Represents a CAN filter rule (表示 CAN 过滤规则的抽象)。
    /// </summary>
    /// <param name="FilterIdType">Frame ID type used by the rule (使用的帧 ID 类型)。</param>
    public abstract record FilterRule(CanFilterIDType FilterIdType)
    {
        /// <summary>
        /// Range-based rule defined by an ID interval (基于 ID 范围的过滤规则)。
        /// </summary>
        /// <param name="From">Minimum ID inclusive (最小 ID，含)。</param>
        /// <param name="To">Maximum ID inclusive (最大 ID，含)。</param>
        public sealed record Range(uint From, uint To) : FilterRule(CanFilterIDType.Standard)
        {
            /// <summary>
            /// Initialize range rule with specific ID type (使用指定 ID 类型初始化范围规则)。
            /// </summary>
            /// <param name="From">Minimum ID inclusive (最小 ID，含)。</param>
            /// <param name="To">Maximum ID inclusive (最大 ID，含)。</param>
            /// <param name="idType">Standard or extended ID (标准/扩展 ID)。</param>
            public Range(uint From, uint To, CanFilterIDType idType) : this(From, To)
            {
                FilterIdType = idType;
            }
        }

        /// <summary>
        /// Mask-based rule defined by acceptance code/mask (基于验收码/屏蔽码的过滤规则)。
        /// </summary>
        /// <param name="AccCode">Acceptance code (验收码)。</param>
        /// <param name="AccMask">Acceptance mask (屏蔽码)。</param>
        public sealed record Mask(uint AccCode, uint AccMask) : FilterRule(CanFilterIDType.Standard)
        {
            /// <summary>
            /// Initialize mask rule with specific ID type (使用指定 ID 类型初始化掩码规则)。
            /// </summary>
            /// <param name="AccCode">Acceptance code (验收码)。</param>
            /// <param name="AccMask">Acceptance mask (屏蔽码)。</param>
            /// <param name="idType">Standard or extended ID (标准/扩展 ID)。</param>
            public Mask(uint AccCode, uint AccMask, CanFilterIDType idType) : this(AccCode, AccMask)
            {
                FilterIdType = idType;
            }
        }
    }

    /// <summary>
    /// Contract for exposing CAN filter rules (用于暴露 CAN 过滤规则的约定)。
    /// </summary>
    public interface ICanFilter
    {
        /// <summary>
        /// Get the list of active filter rules (获取活动过滤规则列表)。
        /// </summary>
        IReadOnlyList<FilterRule> FilterRules { get; }
    }

    /// <summary>
    /// Default CAN filter implementation with mutable list (默认过滤器实现，维护可变规则列表)。
    /// </summary>
    public class CanFilter : ICanFilter
    {
        /// <summary>
        /// Internal list storing actual rules (存储规则的内部列表)。
        /// </summary>
        public List<FilterRule> filterRules = new();

        /// <inheritdoc />
        public IReadOnlyList<FilterRule> FilterRules => filterRules;
    }

}

