using System;
using System.Buffers;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Common;

namespace CanKit.Abstractions.API.Can.Definitions
{

    [Flags]
    public enum FrameFlags : ushort
    {
        None = 0,
        Ext = 1,
        Rtr = 2,
        Brs = 4,
        Esi = 8,
        Error = 16
    }

    public readonly record struct CanFrameView
    {
        private const uint ID_EFF_MASK = 0x1FFFFFFF;
        private const uint ID_STD_MASK = 0x000007FF;

        private readonly int _id;

        /// <summary>
        /// Initializes a read-only view from a frame. (从帧创建只读视图。)
        /// </summary>
        /// <param name="frame">Source frame. (源帧。)</param>
        public CanFrameView(in CanFrame frame)
        {
            FrameKind = frame.FrameKind;
            _id = frame._id;
            Data = frame.Data;
            Flags = frame.Flags;
        }

        /// <summary>
        /// Initializes a read-only view from raw frame parts. (从帧的原始组成部分创建只读视图。)
        /// </summary>
        /// <param name="frameKind">Type of the CAN frame. (CAN 帧类型。)</param>
        /// <param name="rawId">Raw ID value before flag masking. (标志位剥离前的原始 ID 值。)</param>
        /// <param name="data">Payload bytes. (载荷数据。)</param>
        /// <param name="flags">Frame flags. (帧标志位。)</param>
        public CanFrameView(CanFrameType frameKind, int rawId, ReadOnlyMemory<byte> data, FrameFlags flags)
        {
            FrameKind = frameKind;
            _id = rawId;
            Data = data;
            Flags = flags;
        }

        /// <summary>
        /// Gets the actual ID with flag bits stripped. (获取剔除标志位后的实际 ID。)
        /// </summary>
        public int ID => (int)(_id & (IsExtendedFrame ? ID_EFF_MASK : ID_STD_MASK));

        /// <summary>
        /// Type of the CAN frame (Classical CAN 2.0 or CAN FD). (帧类型：CAN 2.0 或 CAN FD。)
        /// </summary>
        public CanFrameType FrameKind { get; }

        /// <summary>
        /// Payload bytes of the frame. (帧的载荷数据。)
        /// </summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <summary>
        /// Bitwise frame flags such as EXT, RTR, BRS, ESI, and Error. (帧的标志位集合，例如 EXT、RTR、BRS、ESI 和 Error。)
        /// </summary>
        public FrameFlags Flags { get; }

        /// <summary>
        /// Data Length Code derived from the payload length. (根据载荷长度计算得到的 DLC。)
        /// </summary>
        public byte Dlc => CanFrame.LenToDlc(Data.Length);

        /// <summary>
        /// Payload length in bytes. (载荷的字节长度。)
        /// </summary>
        public int Len => Data.Length;

        /// <summary>
        /// True if the frame uses an extended 29-bit identifier. (当使用 29 位扩展 ID 时为 true。)
        /// </summary>
        public bool IsExtendedFrame => (Flags & FrameFlags.Ext) != 0;

        /// <summary>
        /// True if the frame is marked as an error frame. (当标记为错误帧时为 true。)
        /// </summary>
        public bool IsErrorFrame => (Flags & FrameFlags.Error) != 0;

        /// <summary>
        /// True if Bit Rate Switching (BRS) is enabled in the data phase. (当数据相位启用速率切换 BRS 时为 true。)
        /// </summary>
        public bool BitRateSwitch => (Flags & FrameFlags.Brs) != 0;

        /// <summary>
        /// True if the transmitter is in Error State (ESI). (当发送端处于错误状态 ESI 时为 true。)
        /// </summary>
        public bool ErrorStateIndicator => (Flags & FrameFlags.Esi) != 0;

        /// <summary>
        /// True if the frame is a Remote (RTR) frame. (当为远程请求帧 RTR 时为 true。)
        /// </summary>
        public bool IsRemoteFrame => (Flags & FrameFlags.Rtr) != 0;
    }


    public readonly record struct CanFrame : IDisposable
    {
        private const uint ID_EFF_MASK = 0x1FFFFFFF;
        private const uint ID_STD_MASK = 0x000007FF;

        private CanFrame(CanFrameType type, int id, bool ownMemory, IMemoryOwner<byte> memoryOwner)
        {
            FrameKind = type;
            _id = id;
            Data = Validate(memoryOwner.Memory);
            OwnMemory = ownMemory;
            _memoryOwner = memoryOwner;
        }

        private CanFrame(CanFrameType type, int id, ReadOnlyMemory<byte> data)
        {
            FrameKind = type;
            _id = id;
            Data = Validate(data);
        }


        private readonly IMemoryOwner<byte>? _memoryOwner;

        internal readonly int _id;

        /// <summary>
        /// Gets or initializes the actual ID with flag bits stripped. (获取或初始化剔除标志位后的实际 ID。)
        /// </summary>
        public int ID => (int)(_id & (IsExtendedFrame ? ID_EFF_MASK : ID_STD_MASK));

        /// <summary>
        /// Type of the CAN frame (Classical CAN 2.0 or CAN FD). (帧类型：CAN 2.0 或 CAN FD)
        /// </summary>
        public CanFrameType FrameKind { get; }

        bool OwnMemory { get; }

        /// <summary>
        /// Payload bytes of the frame. (帧的载荷数据)
        /// </summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <summary>
        /// Bitwise frame flags such as EXT, RTR, BRS, ESI, and Error. (帧的标志位集合，例如 EXT、RTR、BRS、ESI 和 Error)
        /// </summary>
        public FrameFlags Flags { get; init; }

        /// <summary>
        /// Data Length Code derived from the payload length. (根据载荷长度计算得到的 DLC)
        /// </summary>
        public byte Dlc => LenToDlc(Data.Length);

        /// <summary>
        /// Payload length in bytes. (载荷的字节长度)
        /// </summary>
        public int Len => Data.Length;

        /// <summary>
        /// True if the frame uses an extended 29-bit identifier. (当使用 29 位扩展 ID 时为 true)
        /// </summary>
        public bool IsExtendedFrame => (Flags & FrameFlags.Ext) != 0;

        /// <summary>
        /// True if the frame is marked as an error frame. (当标记为错误帧时为 true)
        /// </summary>
        public bool IsErrorFrame => (Flags & FrameFlags.Error) != 0;

        /// <summary>
        /// True if Bit Rate Switching (BRS) is enabled in the data phase. (当数据相位启用速率切换 BRS 时为 true)
        /// </summary>
        public bool BitRateSwitch => (Flags & FrameFlags.Brs) != 0;

        /// <summary>
        /// True if the transmitter is in Error State (ESI). (当发送端处于错误状态 ESI 时为 true)
        /// </summary>
        public bool ErrorStateIndicator => (Flags & FrameFlags.Esi) != 0;

        /// <summary>
        /// True if the frame is a Remote (RTR) frame. (当为远程请求帧 RTR 时为 true)
        /// </summary>
        public bool IsRemoteFrame => (Flags & FrameFlags.Rtr) != 0;

        /// <summary>
        /// Creates a Classical CAN frame from a standard or extended ID. (通过标准/扩展 ID 创建经典帧。)
        /// </summary>
        /// <param name="id">ID without flag bits. (不包含标志位的 ID。)</param>
        /// <param name="dataInit">Frame payload. (帧数据。)</param>
        /// <param name="isExtendedFrame">Indicates whether this is an extended frame. (指示是否为扩展帧。)</param>
        /// <param name="isRemoteFrame">Indicates whether this is an remote frame.（指示是否为远程帧。）</param>
        /// <param name="isErrorFrame"></param>
        public static CanFrame Classic(int id, ReadOnlyMemory<byte> dataInit = default,
            bool isExtendedFrame = false,
            bool isRemoteFrame = false,
            bool isErrorFrame = false)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (!dataInit.IsEmpty && isRemoteFrame) throw new ArgumentOutOfRangeException(nameof(dataInit));

            return new CanFrame(CanFrameType.Can20, id, dataInit)
            {
                Flags = (isRemoteFrame ? FrameFlags.Rtr : 0) | (isExtendedFrame ? FrameFlags.Ext : 0) |
                                                         (isErrorFrame ? FrameFlags.Error : 0)
            };
        }

        /// <summary>
        /// Creates a Classical CAN frame using an existing memory owner for the payload.
        /// 使用外部提供的内存拥有者作为负载来创建经典 CAN 帧。
        /// </summary>
        /// <param name="id">ID without flag bits. ZH: 不包含标志位的 ID。</param>
        /// <param name="memoryOwner">The memory owner providing the payload. ZH: 提供负载数据的内存拥有者。</param>
        /// <param name="isExtendedFrame">Whether this is an extended frame. ZH: 是否为扩展帧。</param>
        /// <param name="isRemoteFrame">Whether this is a remote (RTR) frame. ZH: 是否为远程（RTR）帧。</param>
        /// <param name="ownMemory">If true, disposing the frame disposes <paramref name="memoryOwner"/>.
        /// 若为 true，释放该帧时将同时释放 <paramref name="memoryOwner"/>。</param>
        /// <param name="isErrorFrame"></param>
        public static CanFrame Classic(int id, IMemoryOwner<byte> memoryOwner,
            bool isExtendedFrame = false,
            bool isRemoteFrame = false,
            bool ownMemory = true,
            bool isErrorFrame = false)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            return new CanFrame(CanFrameType.Can20, id, ownMemory, memoryOwner)
            {
                Flags = (isRemoteFrame ? FrameFlags.Rtr : 0) | (isExtendedFrame ? FrameFlags.Ext : 0) |
                        (isErrorFrame ? FrameFlags.Error : 0)
            };
        }

        /// <summary>
        /// Initializes a CAN FD frame with a ID. (通过原始 ID 初始化 CAN FD 帧。)
        /// </summary>
        /// <param name="id">ID without flag bits. (不包含标志位的 ID。)</param>
        /// <param name="dataInit">Frame payload. (帧数据。)</param>
        /// <param name="BRS">Indicates whether Bit Rate Switching (BRS).（是否启用BRS。）</param>
        /// <param name="ESI">ndicates whether the transmitter is in Error State.（发送方是否处于错误状态。）</param>
        /// <param name="isExtendedFrame">Indicates whether this is an extended frame. (指示是否为扩展帧。)</param>
        /// <param name="isErrorFrame"></param>
        public static CanFrame Fd(int id, ReadOnlyMemory<byte> dataInit = default,
            bool BRS = false, bool ESI = false, bool isExtendedFrame = false,
            bool isErrorFrame = false)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            return new CanFrame(CanFrameType.CanFd, id, dataInit)
            {
                Flags = (BRS ? FrameFlags.Brs : 0) | (ESI ? FrameFlags.Esi : 0) | (isExtendedFrame ? FrameFlags.Ext : 0) |
                        (isErrorFrame ? FrameFlags.Error : 0)
            };
        }

        /// <summary>
        /// Creates a CAN FD frame using an existing memory owner for the payload.
        /// 使用外部提供的内存拥有者作为负载来创建 CAN FD 帧。
        /// </summary>
        /// <param name="id">ID without flag bits. ZH: 不包含标志位的 ID。</param>
        /// <param name="memoryOwner">The memory owner providing the payload. ZH: 提供负载数据的内存拥有者。</param>
        /// <param name="BRS">Enable Bit Rate Switching in data phase. ZH: 数据阶段是否启用 BRS。</param>
        /// <param name="ESI">Transmitter in Error State Indicator. ZH: 发送端错误状态指示。</param>
        /// <param name="isExtendedFrame">Whether this is an extended frame. ZH: 是否为扩展帧。</param>
        /// <param name="ownMemory">If true, disposing the frame disposes <paramref name="memoryOwner"/>.
        /// 若为 true，释放该帧时将同时释放 <paramref name="memoryOwner"/>。</param>
        /// <param name="isErrorFrame"></param>
        public static CanFrame Fd(int id, IMemoryOwner<byte> memoryOwner,
            bool BRS = false, bool ESI = false, bool isExtendedFrame = false, bool ownMemory = true,
            bool isErrorFrame = false)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            return new CanFrame(CanFrameType.CanFd, id, ownMemory, memoryOwner)
            {
                Flags = (BRS ? FrameFlags.Brs : 0) | (ESI ? FrameFlags.Esi : 0) | (isExtendedFrame ? FrameFlags.Ext : 0) |
                        (isErrorFrame ? FrameFlags.Error : 0)
            };
        }


        /// <summary>
        /// Creates a frame with the specified flags and payload. (使用指定标志与载荷创建帧)
        /// </summary>
        /// <param name="id">ID without flag bits. (不包含标志位的 ID)</param>
        /// <param name="flags">Frame flags to apply. (要应用的帧标志)</param>
        /// <param name="dataInit">Frame payload. (帧的载荷数据)</param>
        public static CanFrame Create(int id, FrameFlags flags, ReadOnlyMemory<byte> dataInit = default)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            return new CanFrame(CanFrameType.CanFd, id, dataInit)
            {
                Flags = flags
            };
        }

        /// <summary>
        /// Creates a frame with the specified flags using an external memory owner for the payload.
        /// 使用外部提供的内存所有者作为载荷并应用指定标志创建帧。
        /// </summary>
        /// <param name="id">ID without flag bits. (不包含标志位的 ID)</param>
        /// <param name="flags">Frame flags to apply. (要应用的帧标志)</param>
        /// <param name="memoryOwner">Memory owner that holds the payload. (承载载荷数据的内存所有者)</param>
        /// <param name="ownMemory">If true, disposing the frame disposes <paramref name="memoryOwner"/>. (若为 true，释放帧时同时释放 <paramref name="memoryOwner"/>)</param>
        public static CanFrame Create(int id, FrameFlags flags, IMemoryOwner<byte> memoryOwner, bool ownMemory = true)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            return new CanFrame(CanFrameType.CanFd, id, ownMemory, memoryOwner)
            {
                Flags = flags
            };
        }

        /// <summary>
        /// Converts a DLC value to the actual payload length. (将 DLC 值转换为实际的数据长度。)
        /// </summary>
        public static int DlcToLen(byte dlc)
            => dlc <= 8 ? dlc : dlc switch
            {
                9 => 12,
                10 => 16,
                11 => 20,
                12 => 24,
                13 => 32,
                14 => 48,
                15 => 64,
                _ => throw new ArgumentOutOfRangeException(nameof(dlc))
            };

        /// <summary>
        /// Converts payload length to DLC. (将数据长度转换为 DLC。)
        /// </summary>
        public static byte LenToDlc(int len)
        {
            if (len < 0 || len > 64) throw new ArgumentOutOfRangeException(nameof(len));
            if (len <= 8) return (byte)len;
            return len switch
            {
                <= 12 => 9,
                <= 16 => 10,
                <= 20 => 11,
                <= 24 => 12,
                <= 32 => 13,
                <= 48 => 14,
                _ => 15,
            };
        }

        /// <summary>
        /// Validates that CAN FD payload length does not exceed the specification. (校验 CAN FD 数据长度不超过规范限制。)
        /// </summary>
        private ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> src)
        {
            if (FrameKind == CanFrameType.Can20 && src.Length > 8)
                throw new ArgumentOutOfRangeException($"payload:{src.Length}");

            _ = LenToDlc(src.Length); // trigger range check (触发范围检查)
            return src;
        }

        /// <summary>
        /// Creates an independent copy of this frame backed by a freshly rented buffer, so the
        /// copy's lifetime (and <see cref="Dispose"/>) is fully decoupled from this frame's memory
        /// owner. Used to hand out per-consumer copies when the same logical frame is delivered to
        /// multiple independent owners (see the frame ownership contract in
        /// docs/architecture/arc42-CanKit.md §8.1).
        /// 创建该帧的独立副本：副本使用新租借的缓冲区，其生命周期（及 <see cref="Dispose"/>）与原帧的内存所有者完全解耦。
        /// 用于将同一逻辑帧分发给多个独立所有者时，为每个消费者提供各自的副本
        /// （参见 docs/architecture/arc42-CanKit.md §8.1 中的帧所有权契约）。
        /// </summary>
        /// <param name="allocator">Allocator used to rent the copy's backing buffer. (用于为副本租借底层缓冲区的分配器。)</param>
        /// <remarks>
        /// Named <c>Duplicate</c> rather than <c>Clone</c>: <see cref="CanFrame"/> is a
        /// <c>record struct</c>, and the C# compiler reserves the member name <c>Clone</c> for its
        /// own synthesized copy machinery (used by <c>with</c> expressions) — declaring a member
        /// called <c>Clone</c> on a record is a compile error (CS8859).
        /// 命名为 <c>Duplicate</c> 而非 <c>Clone</c>：<see cref="CanFrame"/> 是 <c>record struct</c>，
        /// C# 编译器为其自身合成的拷贝机制（供 <c>with</c> 表达式使用）保留了 <c>Clone</c> 这个成员名，
        /// 在 record 上声明名为 <c>Clone</c> 的成员会导致编译错误（CS8859）。
        /// </remarks>
        public CanFrame Duplicate(IBufferAllocator allocator)
        {
            if (allocator is null) throw new ArgumentNullException(nameof(allocator));
            var owner = allocator.Rent(Data.Length);
            Data.Span.CopyTo(owner.Memory.Span);
            return new CanFrame(FrameKind, _id, allocator.FrameNeedDispose, owner)
            {
                Flags = Flags
            };
        }

        /// <summary>
        /// Releases the owned memory if this frame owns its payload memory. (若该帧拥有其载荷内存，则释放该内存)
        /// </summary>
        /// <remarks>
        /// Only frames created with <c>ownMemory: true</c> (the default for the memory-owner
        /// factory overloads and <see cref="Duplicate"/>) actually release the backing
        /// <see cref="IMemoryOwner{T}"/>; frames that do not own their memory (plain
        /// <see cref="ReadOnlyMemory{T}"/> payloads, or explicit <c>ownMemory: false</c>) treat
        /// <see cref="Dispose"/> as a no-op, per the frame ownership contract in
        /// docs/architecture/arc42-CanKit.md §8.1.
        /// 仅当帧以 <c>ownMemory: true</c> 创建时（内存所有者工厂重载及 <see cref="Duplicate"/> 的默认值），
        /// 才会真正释放底层 <see cref="IMemoryOwner{T}"/>；不拥有内存的帧（普通 <see cref="ReadOnlyMemory{T}"/>
        /// 载荷，或显式 <c>ownMemory: false</c>）将 <see cref="Dispose"/> 视为空操作，
        /// 参见 docs/architecture/arc42-CanKit.md §8.1 中的帧所有权契约。
        /// <para>
        /// Because <see cref="CanFrame"/> is a value type, copies of an owning frame share the
        /// same <see cref="IMemoryOwner{T}"/>; callers must ensure at most one copy per frame
        /// lineage is disposed. The built-in allocators' owners tolerate redundant disposal
        /// (idempotent), but a custom <see cref="IBufferAllocator"/> is not guaranteed to.
        /// 由于 <see cref="CanFrame"/> 是值类型，拥有内存的帧的多个副本会共享同一个
        /// <see cref="IMemoryOwner{T}"/>；调用方需确保同一帧谱系最多只释放一次。内置分配器的
        /// Owner 容忍重复释放（幂等），但自定义 <see cref="IBufferAllocator"/> 不保证如此。
        /// </para>
        /// </remarks>
        public void Dispose()
        {
            if (OwnMemory) _memoryOwner?.Dispose();
        }
    }
}
