using System;

namespace CanKit.Core.Definitions
{

    /// <summary>
    /// 描述 CAN 帧的最小数据集合。
    /// </summary>
    public interface ICanFrame
    {
        /// <summary>
        /// 获取帧的类型，例如经典 CAN 或 CAN FD。
        /// </summary>
        CanFrameType FrameKind { get; }

        /// <summary>
        /// 获取或初始化包含所有标志位的原始 ID。
        /// </summary>
        uint RawID { get; init; }

        /// <summary>
        /// 获取或初始化帧数据。
        /// </summary>
        ReadOnlyMemory<byte> Data { get; init; }

        /// <summary>
        /// 获取帧的 DLC 值。
        /// </summary>
        byte Dlc { get; }

        /// <summary>
        /// 获取或初始化剔除标志位后的实际 ID。
        /// </summary>
        uint ID { get; init; }

        /// <summary>
        /// 是否时错误帧
        /// </summary>
        public bool IsErrorFrame { get; }
    }

    /// <summary>
    /// 用于描述错误帧的附加信息。
    /// </summary>
    public interface ICanErrorInfo
    {
        /// <summary>
        /// 获取错误类型。
        /// </summary>
        FrameErrorType Type { get; init; }

        /// <summary>
        /// 控制器状态（来自错误帧 data[1] 或驱动报告）。
        /// </summary>
        CanControllerStatus ControllerStatus { get; init; }

        /// <summary>
        /// 协议违规类型（来自错误帧 data[2]）。
        /// </summary>
        CanProtocolViolationType ProtocolViolation { get; init; }

        /// <summary>
        /// 协议违规发生位置（来自错误帧 data[3]）。
        /// </summary>
        FrameErrorLocation ProtocolViolationLocation { get; init; }

        /// <summary>
        /// 收发器状态（来自错误帧 data[4]）。
        /// </summary>
        CanTransceiverStatus TransceiverStatus { get; init; }

        /// <summary>
        /// 获取系统时间戳。
        /// </summary>
        DateTime SystemTimestamp { get; init; }

        /// <summary>
        /// 获取原始错误码。
        /// </summary>
        uint RawErrorCode { get; init; }

        /// <summary>
        /// 获取设备时间偏移量。
        /// </summary>
        ulong? TimeOffset { get; init; }

        /// <summary>
        /// 获取错误帧的方向。
        /// </summary>
        FrameDirection Direction { get; init; }

        /// <summary>
        /// 仲裁丢失位（0-31）。若未知则为 null。
        /// Arbitration lost bit position (0-31). Null if unknown.
        /// </summary>
        byte? ArbitrationLostBit { get; init; }


        /// <summary>
        /// 总线错误计数
        /// </summary>
        CanErrorCounters? ErrorCounters { get; init; }

        /// <summary>
        /// 获取相关的原始帧。
        /// </summary>
        ICanFrame? Frame { get; init; }
    }

    /// <summary>
    /// 对 CAN ID 的位操作进行了封装，方便统一处理。
    /// </summary>
    internal static class CanIdBits
    {
        private const int EXT_BIT = 31;
        private const int RTR_BIT = 30;
        private const int ERR_BIT = 29;
        private const uint ID_MASK = 0x1FFFFFFF;

        /// <summary>
        /// 获取去掉标志位后的 ID。
        /// </summary>
        public static uint GetId(uint raw) => raw & ID_MASK;

        /// <summary>
        /// 将 ID 写入原始值中。
        /// </summary>
        public static uint SetId(uint raw, uint id) => (raw & ~ID_MASK) | (id & ID_MASK);

        public static bool Get(uint raw, int bit) => (raw & (1u << bit)) != 0;

        public static uint Set(uint raw, int bit, bool v)
            => v ? (raw | (1u << bit)) : (raw & ~(1u << bit));

        public static bool IsExtended(uint raw) => Get(raw, EXT_BIT);
        public static uint WithExtended(uint raw, bool v) => Set(raw, EXT_BIT, v);
        public static bool IsRemote(uint raw) => Get(raw, RTR_BIT);
        public static uint WithRemote(uint raw, bool v) => Set(raw, RTR_BIT, v);
    }

    /// <summary>
    /// 经典 CAN 帧的值类型实现。
    /// </summary>
    public readonly record struct CanClassicFrame : ICanFrame
    {
        private readonly ReadOnlyMemory<byte> _data;

        /// <summary>
        /// 通过原始 ID 和数据创建经典帧。
        /// </summary>
        public CanClassicFrame(uint rawIDInit, ReadOnlyMemory<byte> dataInit = default)
        {
            RawID = rawIDInit;
            _data = dataInit;
        }

        /// <summary>
        /// 通过标准/扩展 ID 创建经典帧。
        /// </summary>
        /// <param name="Id">不包含标志位的 ID。</param>
        /// <param name="dataInit">帧数据。</param>
        /// <param name="isExtendedFrame">指示是否为扩展帧。</param>
        public CanClassicFrame(uint Id, ReadOnlyMemory<byte> dataInit = default, bool isExtendedFrame = false)
        {
            ID = Id;
            IsExtendedFrame = isExtendedFrame;
            _data = dataInit;
        }

        public bool IsExtendedFrame
        {
            get => CanIdBits.IsExtended(RawID);
            init => RawID = CanIdBits.WithExtended(RawID, value);
        }

        public bool IsRemoteFrame
        {
            get => CanIdBits.IsRemote(RawID);
            init => RawID = CanIdBits.WithRemote(RawID, value);
        }

        public bool IsErrorFrame { get; init; }

        public CanFrameType FrameKind => CanFrameType.Can20;

        public uint RawID { get; init; }

        public uint ID
        {
            get => CanIdBits.GetId(RawID);
            init => RawID = CanIdBits.SetId(RawID, value);
        }

        /// <summary>
        /// 获取或设置帧数据，同时执行长度校验。
        /// </summary>
        public ReadOnlyMemory<byte> Data
        {
            get => _data;
            init => _data = Validate(value);

        }

        public byte Dlc => (byte)Data.Length;

        /// <summary>
        /// 允许直接将经典帧用作发送数据结构。
        /// </summary>
        public static implicit operator CanTransmitData(CanClassicFrame value)
        {
            return new CanTransmitData(value);
        }

        /// <summary>
        /// 校验经典帧数据长度不超过 8 字节。
        /// </summary>
        private static ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> src)
        {
            if (src.Length > 8) throw new ArgumentOutOfRangeException(nameof(Data),
                "Classic CAN frame data length cannot exceed 8 bytes.");
            return src;
        }
    }



    /// <summary>
    /// CAN FD 帧的值类型实现。
    /// </summary>
    public readonly struct CanFdFrame : ICanFrame
    {
        /// <summary>
        /// 通过原始 ID 初始化 CAN FD 帧。
        /// </summary>
        public CanFdFrame(uint rawIdInit, ReadOnlyMemory<byte> dataInit = default, bool BRS = false, bool ESI = false)
        {
            RawID = rawIdInit;
            _data = Validate(dataInit);
            BitRateSwitch = BRS;
            ErrorStateIndicator = ESI;
        }
        public CanFrameType FrameKind => CanFrameType.CanFd;

        public uint RawID { get; init; }

        public uint ID
        {
            get => CanIdBits.GetId(RawID);
            init => RawID = CanIdBits.SetId(RawID, value);
        }

        public bool IsExtendedFrame
        {
            get => CanIdBits.IsExtended(RawID);
            init => RawID = CanIdBits.WithExtended(RawID, value);
        }
        public bool IsRemoteFrame
        {
            get => CanIdBits.IsRemote(RawID);
            init => RawID = CanIdBits.WithRemote(RawID, value);
        }
        public bool IsErrorFrame { get; init; }

        /// <summary>
        /// 指示该帧在数据阶段是否启用了速率切换 (BRS)。
        /// </summary>
        public bool BitRateSwitch { get; init; }
        /// <summary>
        /// 指示发送方是否处于错误状态 (ESI)。
        /// </summary>
        public bool ErrorStateIndicator { get; init; }

        public ReadOnlyMemory<byte> Data
        {
            get => _data;
            init => _data = Validate(value);
        }

        public byte Dlc => LenToDlc(Data.Length);

        /// <summary>
        /// 将 DLC 值转换为实际的数据长度。
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
        /// 将数据长度转换为 DLC。
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
        /// 校验 CAN FD 数据长度不超过规范限制。
        /// </summary>
        private static ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> src)
        {
            _ = LenToDlc(src.Length); // 触发范围检查
            return src;
        }

        private readonly ReadOnlyMemory<byte> _data;
    }

    /// <summary>
    /// 默认的错误信息实现，直接复用接口定义的字段。
    /// </summary>
    public record DefaultCanErrorInfo(
        FrameErrorType Type,
        CanControllerStatus ControllerStatus,
        CanProtocolViolationType ProtocolViolation,
        FrameErrorLocation ProtocolViolationLocation,
        DateTime SystemTimestamp,
        uint RawErrorCode,
        ulong? TimeOffset,
        FrameDirection Direction,
        byte? ArbitrationLostBit,
        CanTransceiverStatus TransceiverStatus,
        CanErrorCounters? ErrorCounters,
        ICanFrame? Frame) : ICanErrorInfo;
}
