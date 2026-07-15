using System;
using System.Threading;
using System.Threading.Tasks;

namespace CanKit.Pro.RawCan
{
    /// <summary>
    /// Identifies a pending echo-matched send by the two fields an echo frame is matched against:
    /// CAN ID and payload content. Multiple concurrent sends with an identical key are matched
    /// strictly FIFO (oldest pending send first) against arriving echoes with the same key —
    /// see <see cref="CanBusService"/>'s pending-send tracking (FR-RAW-031). This mirrors, at the
    /// L2 echo-matching layer, the exact class of bug the review flagged for the ISO-TP prototype's
    /// deadline queue crashing on identical in-flight frames.
    /// (通过 CAN ID 与载荷内容标识一路等待回显匹配的发送。多路并发、键相同的发送与到达的、键相同的回显帧
    /// 严格按 FIFO（最先等待的发送优先）匹配。)
    /// </summary>
    internal readonly struct PendingKey : IEquatable<PendingKey>
    {
        private readonly int _id;
        private readonly byte[] _payload;
        private readonly int _hash;

        public PendingKey(int id, ReadOnlyMemory<byte> payload)
        {
            _id = id;
            _payload = payload.ToArray();

            // Manual combine: System.HashCode isn't available on netstandard2.0 without an extra
            // package reference, and this hash never needs to be cryptographically strong.
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _id;
                foreach (var b in _payload)
                    hash = hash * 31 + b;
                _hash = hash;
            }
        }

        public bool Equals(PendingKey other)
            => _id == other._id && _payload.AsSpan().SequenceEqual(other._payload);

        public override bool Equals(object? obj) => obj is PendingKey other && Equals(other);

        public override int GetHashCode() => _hash;
    }

    /// <summary>
    /// One outstanding <see cref="ICanBusService.SendConfirmed"/> call awaiting an echo match.
    /// Owns the node reference into its <see cref="PendingKey"/>'s FIFO list so it can remove
    /// itself in O(1) on any resolution path (match, timeout, cancellation, bus-off, or service
    /// disposal) without scanning. Exactly one of those paths ever completes <see cref="Tcs"/>;
    /// all use <c>TrySet*</c>, so a race between two paths (e.g. an echo arriving the same instant
    /// the timeout fires) resolves harmlessly to whichever wins first.
    /// (一次等待回显匹配的、尚未完成的 <see cref="ICanBusService.SendConfirmed"/> 调用。持有其在所属
    /// <see cref="PendingKey"/> FIFO 链表中的节点引用，以便在匹配、超时、取消、总线关闭或服务释放等任一
    /// 结束路径下以 O(1) 复杂度自我移除，无需遍历。)
    /// </summary>
    internal sealed class PendingSend
    {
        public PendingSend(PendingKey key)
        {
            Key = key;
        }

        public PendingKey Key { get; }

        public TaskCompletionSource<TxConfirmation> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Node in the owning <see cref="System.Collections.Generic.LinkedList{T}"/> for this
        /// pending send's <see cref="Key"/>. Only ever touched under
        /// <see cref="CanBusService"/>'s pending-registry lock. Null once removed.
        /// </summary>
        public System.Collections.Generic.LinkedListNode<PendingSend>? Node { get; set; }
    }
}
