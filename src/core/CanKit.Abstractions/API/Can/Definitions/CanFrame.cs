using System;
using System.Buffers;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Abstractions.API.Can.Definitions
{

    [Flags]
    public enum FrameFlags : ushort
    {
        None = 0, Ext = 1, Rtr = 2, Brs = 4, Esi = 8, Error = 16
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

        private readonly int _id;

        /// <summary>
        /// Gets or initializes the actual ID with flag bits stripped. (获取或初始化剔除标志位后的实际 ID。)
        /// </summary>
        public int ID => (int)(_id & (IsExtendedFrame ? ID_EFF_MASK : ID_STD_MASK));

        public CanFrameType FrameKind { get; }

        bool OwnMemory { get; }

        public ReadOnlyMemory<byte> Data { get; }

        public FrameFlags Flags { get; init; }

        public byte Dlc => LenToDlc(Data.Length);

        public int Len => Data.Length;

        public bool IsExtendedFrame => (Flags & FrameFlags.Ext) != 0;

        public bool IsErrorFrame => (Flags & FrameFlags.Error) != 0;

        public bool BitRateSwitch => (Flags & FrameFlags.Brs) != 0;

        public bool ErrorStateIndicator => (Flags & FrameFlags.Esi) != 0;

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
            return new CanFrame(CanFrameType.Can20, id, dataInit)
            {
                Flags = isRemoteFrame ? FrameFlags.Rtr : 0 | (isExtendedFrame ? FrameFlags.Ext : 0) |
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


        public static CanFrame Create(int id, FrameFlags flags, ReadOnlyMemory<byte> dataInit = default)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            return new CanFrame(CanFrameType.CanFd, id, dataInit)
            {
                Flags = flags
            };
        }

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
                throw new ArgumentOutOfRangeException("payload");

            _ = LenToDlc(src.Length); // trigger range check (触发范围检查)
            return src;
        }

        public void Dispose() => _memoryOwner?.Dispose();
    }
}
