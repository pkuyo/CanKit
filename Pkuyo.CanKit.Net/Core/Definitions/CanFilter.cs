using System.Collections.Generic;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    /// <summary>
    /// 表示一条 CAN 过滤规则的抽象基类。
    /// </summary>
    /// <param name="FilterIdType">指示过滤规则使用的帧 ID 类型。</param>
    public abstract record FilterRule(CanFilterIDType FilterIdType)
    {
        /// <summary>
        /// 通过设定 ID 范围来进行过滤的规则。
        /// </summary>
        /// <param name="From">允许的最小 ID。</param>
        /// <param name="To">允许的最大 ID。</param>
        public sealed record Range(uint From, uint To) : FilterRule(CanFilterIDType.Standard)
        {
            /// <summary>
            /// 使用指定的 ID 类型初始化范围过滤规则。
            /// </summary>
            /// <param name="From">允许的最小 ID。</param>
            /// <param name="To">允许的最大 ID。</param>
            /// <param name="idType">标准帧或扩展帧。</param>
            public Range(uint From, uint To, CanFilterIDType idType) : this(From, To)
            {
                FilterIdType = idType;
            }
        }

        /// <summary>
        /// 通过设定验收码和掩码来匹配的过滤规则。
        /// </summary>
        /// <param name="AccCode">验收码。</param>
        /// <param name="AccMask">掩码。</param>
        public sealed record Mask(uint AccCode, uint AccMask) : FilterRule(CanFilterIDType.Standard)
        {
            /// <summary>
            /// 使用指定的 ID 类型初始化掩码过滤规则。
            /// </summary>
            /// <param name="AccCode">验收码。</param>
            /// <param name="AccMask">掩码。</param>
            /// <param name="idType">标准帧或扩展帧。</param>
            public Mask(uint AccCode, uint AccMask, CanFilterIDType idType) : this(AccCode, AccMask)
            {
                FilterIdType = idType;
            }
        }
    }

    /// <summary>
    /// 定义 CAN 过滤器对外暴露的最小契约。
    /// </summary>
    public interface ICanFilter
    {
        /// <summary>
        /// 获取过滤器包含的过滤规则集合。
        /// </summary>
        IReadOnlyList<FilterRule> FilterRules { get; }
    }

    /// <summary>
    /// 默认的 CAN 过滤器实现，内部维护可变的规则列表。
    /// </summary>
    public class CanFilter : ICanFilter
    {
        /// <summary>
        /// 实际保存过滤规则的可变列表。
        /// </summary>
        public List<FilterRule> filterRules = new();

        /// <inheritdoc />
        public IReadOnlyList<FilterRule> FilterRules => filterRules;
    }

}