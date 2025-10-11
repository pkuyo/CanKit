using System;
using System.Collections.Generic;
using System.Linq;
using CanKit.Core.Abstractions;

namespace CanKit.Core.Definitions
{
    /// <summary>
    /// Represents a CAN filter rule (表示 CAN 过滤规则的抽象)
    /// </summary>
    /// <param name="FilterIdType">Frame ID type used by the rule (使用的帧 ID 类型)</param>
    public abstract record FilterRule(CanFilterIDType FilterIdType)
    {
        /// <summary>
        /// Build a predicate that checks whether a frame matches this rule.
        /// 构建用于判断帧是否匹配该规则的谓词函数。
        /// </summary>
        public abstract Func<ICanFrame, bool> Build();

        /// <summary>
        /// Combine multiple rules into a unified predicate (OR semantics).
        /// 将多个规则合成为一个统一的过滤函数（OR 语义）。
        /// </summary>
        public static Func<ICanFrame, bool> Build(IEnumerable<FilterRule> rules)
        {
            var list = rules?.ToArray() ?? [];
            if (list.Length == 0)
                return _ => true; // no filter -> accept all

            var predicates = list.Select(r => r.Build()).ToArray();
            return frame =>
            {
                foreach (var func in predicates)
                {
                    if (func(frame))
                    {
                        return true;
                    }
                }

                return false;
            };
        }

        /// <summary>
        /// Range-based rule defined by an ID interval (基于 ID 范围的过滤规则)
        /// </summary>
        /// <param name="From">Minimum ID inclusive (最小 ID，含)</param>
        /// <param name="To">Maximum ID inclusive (最大 ID，含)</param>
        public sealed record Range(uint From, uint To) : FilterRule(CanFilterIDType.Standard)
        {
            /// <summary>
            /// Initialize range rule with specific ID type (使用指定 ID 类型初始化范围规则)
            /// </summary>
            /// <param name="From">Minimum ID inclusive (最小 ID，含)</param>
            /// <param name="To">Maximum ID inclusive (最大 ID，含)</param>
            /// <param name="idType">Standard or extended ID (标准/扩展 ID)</param>
            public Range(uint From, uint To, CanFilterIDType idType) : this(From, To)
            {
                FilterIdType = idType;
            }

            public override Func<ICanFrame, bool> Build()
            {
                return frame =>
                {
                    if ((FilterIdType == CanFilterIDType.Extend) != frame.IsExtendedFrame)
                    {
                        return false;
                    }

                    var id = frame.ID;
                    return id >= From && id <= To;
                };
            }
        }

        /// <summary>
        /// Mask-based rule defined by acceptance code/mask (基于验收码/屏蔽码的过滤规则)
        /// </summary>
        /// <param name="AccCode">Acceptance code (验收码)</param>
        /// <param name="AccMask">Acceptance mask (屏蔽码)</param>
        public sealed record Mask(uint AccCode, uint AccMask) : FilterRule(CanFilterIDType.Standard)
        {
            /// <summary>
            /// Initialize mask rule with specific ID type (使用指定 ID 类型初始化掩码规则)
            /// </summary>
            /// <param name="AccCode">Acceptance code (验收码)</param>
            /// <param name="AccMask">Acceptance mask (屏蔽码)</param>
            /// <param name="idType">Standard or extended ID (标准/扩展 ID)</param>
            public Mask(uint AccCode, uint AccMask, CanFilterIDType idType) : this(AccCode, AccMask)
            {
                FilterIdType = idType;
            }

            public override Func<ICanFrame, bool> Build()
            {
                return frame =>
                {
                    if ((FilterIdType == CanFilterIDType.Extend) != frame.IsExtendedFrame) return false;
                    var id = frame.ID;
                    return (id & AccMask) == (AccCode & AccMask);
                };
            }
        }
    }


    /// <summary>
    /// Contract for exposing CAN filter rules (用于暴露 CAN 过滤规则的约定)
    /// </summary>
    public class CanFilter : ICanFilter
    {
        /// <summary>
        /// Internal list storing actual hardware-intended rules (硬件过滤器规则列表)
        /// </summary>
        public List<FilterRule> filterRules = new();

        /// <summary>
        /// Software-only filter rules (软过滤规则，用于硬件不支持的类型)
        /// </summary>
        public List<FilterRule> softwareFilter = new();

        /// <summary>
        /// Get the list of active filter rules (获取活动过滤规则列表)
        /// </summary>
        public IReadOnlyList<FilterRule> FilterRules => filterRules;

        /// <summary>
        /// Software-only filter rules (软过滤规则，用于硬件不支持的类型)
        /// </summary>
        public IReadOnlyList<FilterRule> SoftwareFilterRules => softwareFilter;
    }

}
