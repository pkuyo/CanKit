using System;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Pro.RawCan
{
    /// <summary>
    /// Allocation-free ID-range/mask filter for a <see cref="ISubscription"/> (FR-RAW-013,
    /// "Should"). This is the fast path for the common case "one 11/29-bit CAN-ID range (or
    /// acceptance code/mask) per protocol instance": it is evaluated directly against the
    /// read-only <see cref="CanFrameView"/> that the demux layer carries, without allocating or
    /// invoking a generic <see cref="Func{T,TResult}"/> delegate per frame.
    /// 用于订阅的免分配 ID 范围/掩码过滤器（FR-RAW-013，Should）。针对“每个协议实例一个 11/29 位
    /// CAN-ID 范围（或验收码/掩码）”的常见场景，直接对解复用层携带的只读 <see cref="CanFrameView"/>
    /// 求值，避免每帧分配并调用泛型委托。
    /// </summary>
    /// <remarks>
    /// The match logic (ID-type/extended guard + inclusive range / acceptance-mask compare)
    /// intentionally mirrors <c>CanKit.Core.Definitions.FilterRule.Range</c> and
    /// <c>FilterRule.Mask</c> so this package does not introduce a parallel filter vocabulary.
    /// It is replicated here rather than reused because those rules compile to a
    /// <c>Func&lt;CanFrame, bool&gt;</c> that operates on the disposable <see cref="CanFrame"/>,
    /// whereas the demux layer only ever exposes the non-owning <see cref="CanFrameView"/>; the
    /// range/mask check itself is a couple of integer comparisons, so replicating it is both
    /// simpler and cheaper than converting a view back into a frame per match. The
    /// <see cref="CanFilterIDType"/> vocabulary is reused as-is.
    /// </remarks>
    public readonly struct CanIdFilter
    {
        private enum Kind : byte
        {
            Range = 0,
            Mask = 1,
        }

        private readonly Kind _kind;

        // Range: [_a.._b] inclusive. Mask: _a = acceptance code, _b = acceptance mask.
        private readonly uint _a;
        private readonly uint _b;

        private CanIdFilter(Kind kind, uint a, uint b, CanFilterIDType idType)
        {
            _kind = kind;
            _a = a;
            _b = b;
            IdType = idType;
        }

        /// <summary>
        /// ID space this filter targets (standard 11-bit vs. extended 29-bit). Frames of the other
        /// ID space never match. (该过滤器作用的 ID 空间：标准 11 位或扩展 29 位；另一空间的帧不匹配。)
        /// </summary>
        public CanFilterIDType IdType { get; }

        /// <summary>
        /// Creates an inclusive ID-range filter [<paramref name="from"/>..<paramref name="to"/>].
        /// (创建包含端点的 ID 范围过滤器。)
        /// </summary>
        /// <param name="from">Minimum ID, inclusive. (最小 ID，含。)</param>
        /// <param name="to">Maximum ID, inclusive. (最大 ID，含。)</param>
        /// <param name="idType">Standard or extended ID space. (标准或扩展 ID 空间。)</param>
        public static CanIdFilter Range(uint from, uint to, CanFilterIDType idType = CanFilterIDType.Standard)
        {
            if (to < from) throw new ArgumentException("'to' must be greater than or equal to 'from'.", nameof(to));
            return new CanIdFilter(Kind.Range, from, to, idType);
        }

        /// <summary>
        /// Creates an acceptance-code/mask filter: a frame matches when
        /// <c>(id &amp; accMask) == (accCode &amp; accMask)</c>. (创建验收码/掩码过滤器。)
        /// </summary>
        /// <param name="accCode">Acceptance code. (验收码。)</param>
        /// <param name="accMask">Acceptance mask; only the set bits are compared. (屏蔽码；仅比较置位的位。)</param>
        /// <param name="idType">Standard or extended ID space. (标准或扩展 ID 空间。)</param>
        public static CanIdFilter Mask(uint accCode, uint accMask, CanFilterIDType idType = CanFilterIDType.Standard)
            => new CanIdFilter(Kind.Mask, accCode, accMask, idType);

        /// <summary>
        /// Returns true when <paramref name="frame"/> matches this filter. (当帧匹配该过滤器时返回 true。)
        /// </summary>
        public bool Matches(in CanFrameView frame)
        {
            // Mirrors FilterRule.Range/.Mask: reject frames from the other ID space first, then
            // compare the (flag-stripped) ID.
            if ((IdType == CanFilterIDType.Extend) != frame.IsExtendedFrame)
                return false;

            var id = (uint)frame.ID;
            return _kind == Kind.Range
                ? id >= _a && id <= _b
                : (id & _b) == (_a & _b);
        }

        /// <summary>
        /// True if some CAN ID exists that both this filter and <paramref name="other"/> would
        /// match (FR-RAW-041, "Should") -- a diagnostic for catching misconfigured protocol
        /// instances whose subscriptions were meant to have disjoint ID spaces. Filters targeting
        /// different <see cref="IdType"/> spaces (Standard vs. Extended) never overlap, since a
        /// frame is never both. (是否存在某个 CAN ID 同时被本过滤器与 <paramref name="other"/> 匹配——用于诊断
        /// 本应互不重叠、但实际重叠的多个协议实例订阅配置错误。作用于不同 ID 空间（标准/扩展）的过滤器恒不重叠，
        /// 因为一帧不可能同时属于两者。)
        /// </summary>
        public bool Overlaps(CanIdFilter other)
        {
            if (IdType != other.IdType) return false;

            return (_kind, other._kind) switch
            {
                (Kind.Range, Kind.Range) => _a <= other._b && other._a <= _b,
                // Two acceptance-mask filters overlap iff, on every bit position both masks
                // constrain, the two required bit patterns agree -- bit positions constrained by
                // only one filter (or neither) are always satisfiable by some ID.
                (Kind.Mask, Kind.Mask) => (_a & _b & other._b) == (other._a & _b & other._b),
                (Kind.Range, Kind.Mask) => RangeIntersectsMask(_a, _b, other._a, other._b),
                (Kind.Mask, Kind.Range) => RangeIntersectsMask(other._a, other._b, _a, _b),
                _ => false,
            };
        }

        // Does some ID in [lo, hi] satisfy (id & mask) == (code & mask)? Bit-by-bit existence
        // search from the MSB down, tracking whether the prefix built so far is still exactly
        // equal to lo's/hi's prefix ("tight"); once neither bound is tight anymore, every
        // remaining ID satisfying the (now unconstrained-by-range) mask trivially exists, so the
        // search terminates early rather than enumerating actual ID values. Runs in O(bit-width):
        // at most one branch stays "tight" past any given level, so this never actually branches
        // into an exponential search despite the naive-looking recursion.
        private static bool RangeIntersectsMask(uint lo, uint hi, uint code, uint mask)
        {
            return Exists(28, true, true);

            bool Exists(int bit, bool loTight, bool hiTight)
            {
                if (bit < 0) return true;
                if (!loTight && !hiTight) return true;

                var b = 1u << bit;
                var loBit = (lo & b) != 0;
                var hiBit = (hi & b) != 0;
                var masked = (mask & b) != 0;
                var forcedBit = (code & b) != 0;

                bool TryBit(bool v)
                {
                    if (loTight && !v && loBit) return false; // would fall below lo while still tight
                    if (hiTight && v && !hiBit) return false; // would exceed hi while still tight
                    return Exists(bit - 1, loTight && v == loBit, hiTight && v == hiBit);
                }

                return masked ? TryBit(forcedBit) : TryBit(false) || TryBit(true);
            }
        }
    }
}
