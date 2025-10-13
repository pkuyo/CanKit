using System;

namespace CanKit.Core.Definitions
{

    /// <summary>
    /// Minimal data set describing a CAN frame. (描述 CAN 帧的最小数据集合。)
    /// </summary>
    public interface ICanFrame
    {
        /// <summary>
        /// Gets the frame type, e.g., Classical CAN or CAN FD. (获取帧的类型，例如经典 CAN 或 CAN FD。)
        /// </summary>
        CanFrameType FrameKind { get; }

        /// <summary>
        /// Gets or initializes the frame payload data. (获取或初始化帧数据。)
        /// </summary>
        ReadOnlyMemory<byte> Data { get; init; }

        /// <summary>
        /// Gets the frame's DLC value. (获取帧的 DLC 值。)
        /// </summary>
        byte Dlc { get; }

        /// <summary>
        /// Gets or initializes the actual ID with flag bits stripped. (获取或初始化剔除标志位后的实际 ID。)
        /// </summary>
        int ID { get; init; }

        /// <summary>
        /// Indicates whether this is an error frame. (是否为错误帧。)
        /// </summary>
        public bool IsErrorFrame { get; }

        /// <summary>
        /// Indicates whether the frame uses the extended ID format. (指示该帧是否为扩展帧。)
        /// </summary>
        public bool IsExtendedFrame { get; }
    }

    /// <summary>
    /// Additional information describing an error frame. (用于描述错误帧的附加信息。)
    /// </summary>
    public interface ICanErrorInfo
    {
        /// <summary>
        /// Gets the error type. (获取错误类型。)
        /// </summary>
        FrameErrorType Type { get; init; }

        /// <summary>
        /// Controller status. (控制器状态。)
        /// </summary>
        CanControllerStatus ControllerStatus { get; init; }

        /// <summary>
        /// Protocol violation type. (协议违规类型。)
        /// </summary>
        CanProtocolViolationType ProtocolViolation { get; init; }

        /// <summary>
        /// Location where the protocol violation occurred. (协议违规发生位置。)
        /// </summary>
        FrameErrorLocation ProtocolViolationLocation { get; init; }

        /// <summary>
        /// Transceiver status. (收发器状态。)
        /// </summary>
        CanTransceiverStatus TransceiverStatus { get; init; }

        /// <summary>
        /// Gets the system timestamp. (获取系统时间戳。)
        /// </summary>
        DateTime SystemTimestamp { get; init; }

        /// <summary>
        /// Gets the raw error code. (获取原始错误码。)
        /// </summary>
        uint RawErrorCode { get; init; }

        /// <summary>
        /// Gets the device time offset. (获取设备时间偏移量。)
        /// </summary>
        TimeSpan? DeviceTimeSpan { get; init; }

        /// <summary>
        /// Gets the direction of the error frame. (获取错误帧的方向。)
        /// </summary>
        FrameDirection Direction { get; init; }

        /// <summary>
        /// Arbitration lost bit position (0–31). Null if unknown. (仲裁丢失位位置（0–31）。若未知则为 null。)
        /// </summary>
        byte? ArbitrationLostBit { get; init; }

        /// <summary>
        /// Bus error counters. (总线错误计数。)
        /// </summary>
        CanErrorCounters? ErrorCounters { get; init; }

        /// <summary>
        /// Gets the associated raw frame. (获取相关的原始帧。)
        /// </summary>
        ICanFrame? Frame { get; init; }
    }

    /// <summary>
    /// Encapsulates bit operations on CAN IDs for consistent handling. (对 CAN ID 的位操作进行封装，便于统一处理。)
    /// </summary>
    internal static class CanIdBits
    {
        private const int EXT_BIT = 31;
        private const int RTR_BIT = 30;
        private const int ERR_BIT = 29;
        private const uint ID_MASK = 0x1FFFFFFF;

        /// <summary>
        /// Gets the ID with flag bits removed. (获取去掉标志位后的 ID。)
        /// </summary>
        public static int GetId(uint raw) => (int)(raw & ID_MASK);

        /// <summary>
        /// Writes the ID into the raw value. (将 ID 写入原始值中。)
        /// </summary>
        public static uint SetId(uint raw, int id) => (raw & ~ID_MASK) | ((uint)id & ID_MASK);

        public static bool Get(uint raw, int bit) => (raw & (1u << bit)) != 0;

        public static uint Set(uint raw, int bit, bool v)
            => v ? (raw | (1u << bit)) : (raw & ~(1u << bit));

        public static bool IsExtended(uint raw) => Get(raw, EXT_BIT);
        public static uint WithExtended(uint raw, bool v) => Set(raw, EXT_BIT, v);
        public static bool IsRemote(uint raw) => Get(raw, RTR_BIT);
        public static uint WithRemote(uint raw, bool v) => Set(raw, RTR_BIT, v);
    }

    /// <summary>
    /// Value-type implementation of a Classical CAN frame. (经典 CAN 帧的值类型实现。)
    /// </summary>
    public readonly record struct CanClassicFrame : ICanFrame
    {
        private readonly ReadOnlyMemory<byte> _data;


        /// <summary>
        /// Creates a Classical CAN frame from a standard or extended ID. (通过标准/扩展 ID 创建经典帧。)
        /// </summary>
        /// <param name="id">ID without flag bits. (不包含标志位的 ID。)</param>
        /// <param name="dataInit">Frame payload. (帧数据。)</param>
        /// <param name="isExtendedFrame">Indicates whether this is an extended frame. (指示是否为扩展帧。)</param>
        /// <param name="isRemoteFrame">Indicates whether this is an remote frame.（指示是否为远程帧。）</param>
        public CanClassicFrame(int id, ReadOnlyMemory<byte> dataInit = default,
            bool isExtendedFrame = false,
            bool isRemoteFrame = false)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            ID = id;
            IsRemoteFrame = isRemoteFrame;
            IsExtendedFrame = isExtendedFrame;
            _data = dataInit;
        }

        /// <summary>
        /// Indicates whether the frame uses the extended ID format. (指示该帧是否为扩展帧。)
        /// </summary>
        public bool IsExtendedFrame
        {
            get => CanIdBits.IsExtended(RawID);
            init => RawID = CanIdBits.WithExtended(RawID, value);
        }

        /// <summary>
        /// Indicates whether the frame is a remote (RTR) frame. (指示该帧是否为远程帧（RTR）。)
        /// </summary>
        public bool IsRemoteFrame
        {
            get => CanIdBits.IsRemote(RawID);
            init => RawID = CanIdBits.WithRemote(RawID, value);
        }

        /// <summary>
        /// Indicates whether this is an error frame. (指示该帧是否为错误帧。)
        /// </summary>
        public bool IsErrorFrame { get; init; }

        /// <summary>
        /// Gets the frame kind (Classical CAN). (获取帧类型（经典 CAN）。)
        /// </summary>
        public CanFrameType FrameKind => CanFrameType.Can20;

        /// <summary>
        /// Gets or initializes the raw ID containing all flag bits. (获取或初始化包含所有标志位的原始 ID。)
        /// </summary>
        private uint RawID { get; init; }

        /// <summary>
        /// Gets or initializes the actual ID with flag bits stripped. (获取或初始化剔除标志位后的实际 ID。)
        /// </summary>
        public int ID
        {
            get => CanIdBits.GetId(RawID);
            init
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(ID));
                RawID = CanIdBits.SetId(RawID, value);
            }
        }

        /// <summary>
        /// Gets or sets the frame payload while validating its length. (获取或设置帧数据，同时执行长度校验。)
        /// </summary>
        public ReadOnlyMemory<byte> Data
        {
            get => _data;
            init => _data = Validate(value);

        }

        /// <summary>
        /// Gets the DLC as the payload length (0–8). (获取 DLC 值，即负载长度（0–8）。)
        /// </summary>
        public byte Dlc => (byte)Data.Length;

        /// <summary>
        /// Validates that Classical CAN payload length does not exceed 8 bytes. (校验经典帧数据长度不超过 8 字节。)
        /// </summary>
        private static ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> src)
        {
            if (src.Length > 8) throw new ArgumentOutOfRangeException(nameof(Data),
                "Classic CAN frame data length cannot exceed 8 bytes.");
            return src;
        }
    }



    /// <summary>
    /// Value-type implementation of a CAN FD frame. (CAN FD 帧的值类型实现。)
    /// </summary>
    public readonly struct CanFdFrame : ICanFrame
    {
        /// <summary>
        /// Initializes a CAN FD frame with a ID. (通过原始 ID 初始化 CAN FD 帧。)
        /// </summary>
        /// <param name="id">ID without flag bits. (不包含标志位的 ID。)</param>
        /// <param name="dataInit">Frame payload. (帧数据。)</param>
        /// <param name="BRS">Indicates whether Bit Rate Switching (BRS).（是否启用BRS。）</param>
        /// <param name="ESI">ndicates whether the transmitter is in Error State.（发送方是否处于错误状态。）</param>
        /// <param name="isExtendedFrame">Indicates whether this is an extended frame. (指示是否为扩展帧。)</param>
        public CanFdFrame(int id, ReadOnlyMemory<byte> dataInit = default, bool BRS = false, bool ESI = false, bool isExtendedFrame = false)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            ID = id;
            _data = Validate(dataInit);
            IsExtendedFrame = isExtendedFrame;
            BitRateSwitch = BRS;
            ErrorStateIndicator = ESI;
        }

        /// <summary>
        /// Gets the frame kind (CAN FD). (获取帧类型（CAN FD）。)
        /// </summary>
        public CanFrameType FrameKind => CanFrameType.CanFd;

        /// <summary>
        /// Gets or initializes the raw ID containing all flag bits. (获取或初始化包含所有标志位的原始 ID。)
        /// </summary>
        private uint RawID { get; init; }

        /// <summary>
        /// Gets or initializes the actual ID with flag bits stripped. (获取或初始化剔除标志位后的实际 ID。)
        /// </summary>
        public int ID
        {
            get => CanIdBits.GetId(RawID);
            init
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(ID));
                RawID = CanIdBits.SetId(RawID, value);
            }
        }

        /// <summary>
        /// Indicates whether the frame uses the extended ID format. (指示该帧是否为扩展帧。)
        /// </summary>
        public bool IsExtendedFrame
        {
            get => CanIdBits.IsExtended(RawID);
            init => RawID = CanIdBits.WithExtended(RawID, value);
        }

        /// <summary>
        /// Indicates whether this is an error frame. (指示该帧是否为错误帧。)
        /// </summary>
        public bool IsErrorFrame { get; init; }

        /// <summary>
        /// Indicates whether Bit Rate Switching (BRS) is enabled in the data phase. (指示该帧在数据阶段是否启用了速率切换（BRS）。)
        /// </summary>
        public bool BitRateSwitch { get; init; }

        /// <summary>
        /// Indicates whether the transmitter is in Error State (ESI). (指示发送方是否处于错误状态（ESI）。)
        /// </summary>
        public bool ErrorStateIndicator { get; init; }

        /// <summary>
        /// Gets or sets the frame payload while validating its length. (获取或设置帧数据，同时执行长度校验。)
        /// </summary>
        public ReadOnlyMemory<byte> Data
        {
            get => _data;
            init => _data = Validate(value);
        }

        /// <summary>
        /// Gets the DLC corresponding to the payload length. (获取与负载长度对应的 DLC 值。)
        /// </summary>
        public byte Dlc => LenToDlc(Data.Length);

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
        private static ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> src)
        {
            _ = LenToDlc(src.Length); // trigger range check (触发范围检查)
            return src;
        }

        private readonly ReadOnlyMemory<byte> _data;
    }

    /// <summary>
    /// Default error info implementation reusing the interface-defined fields. (默认的错误信息实现，直接复用接口定义的字段。)
    /// </summary>
    public record DefaultCanErrorInfo(
        FrameErrorType Type,
        CanControllerStatus ControllerStatus,
        CanProtocolViolationType ProtocolViolation,
        FrameErrorLocation ProtocolViolationLocation,
        DateTime SystemTimestamp,
        uint RawErrorCode,
        TimeSpan? DeviceTimeSpan,
        FrameDirection Direction,
        byte? ArbitrationLostBit,
        CanTransceiverStatus TransceiverStatus,
        CanErrorCounters? ErrorCounters,
        ICanFrame? Frame) : ICanErrorInfo;
}
