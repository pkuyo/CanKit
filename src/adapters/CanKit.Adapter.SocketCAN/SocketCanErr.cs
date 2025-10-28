using CanKit.Adapter.SocketCAN.Native;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.SocketCAN;

internal static class SocketCanErr
{


    public static FrameErrorType ToFrameErrorType(uint errMask)
    {
        FrameErrorType t = FrameErrorType.None;

        if ((errMask & Libc.CAN_ERR_TX_TIMEOUT) != 0) t |= FrameErrorType.TxTimeout; // 0x00000001
        if ((errMask & Libc.CAN_ERR_LOSTARB) != 0) t |= FrameErrorType.ArbitrationLost; // 0x00000002
        if ((errMask & Libc.CAN_ERR_CRTL) != 0) t |= FrameErrorType.Controller; // 0x00000004
        if ((errMask & Libc.CAN_ERR_PROT) != 0) t |= FrameErrorType.ProtocolViolation; // 0x00000008
        if ((errMask & Libc.CAN_ERR_TRX) != 0) t |= FrameErrorType.TransceiverError; // 0x00000010
        if ((errMask & Libc.CAN_ERR_ACK) != 0) t |= FrameErrorType.AckError; // 0x00000020
        if ((errMask & Libc.CAN_ERR_BUSOFF) != 0) t |= FrameErrorType.BusOff; // 0x00000040
        if ((errMask & Libc.CAN_ERR_BUSERROR) != 0) t |= FrameErrorType.BusError; // 0x00000080
        if ((errMask & Libc.CAN_ERR_RESTARTED) != 0) t |= FrameErrorType.Restarted; // 0x00000100

        // ★ 补：错误计数器类位
        if ((errMask & Libc.CAN_ERR_CNT) != 0) t |= FrameErrorType /* 你定义里新增的 */. /*ErrorCounter*/ Unknown;
        // ↑ 如果你已按之前建议给 FrameErrorType 增加 ErrorCounter，请在此置位；
        //   没加的话，这里可暂不置位或复用 Controller（但建议单独一位以精确对齐规范）

        return t == FrameErrorType.None ? FrameErrorType.Unknown : t;
    }


    public static CanControllerStatus ToControllerStatus(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return CanControllerStatus.None;
        byte c = data[1];
        CanControllerStatus cs = CanControllerStatus.None;

        if ((c & Libc.CAN_ERR_CRTL_RX_OVERFLOW) != 0) cs |= CanControllerStatus.RxOverflow; // 0x01
        if ((c & Libc.CAN_ERR_CRTL_TX_OVERFLOW) != 0) cs |= CanControllerStatus.TxOverflow; // 0x02
        if ((c & Libc.CAN_ERR_CRTL_RX_WARNING) != 0) cs |= CanControllerStatus.RxWarning; // 0x04
        if ((c & Libc.CAN_ERR_CRTL_TX_WARNING) != 0) cs |= CanControllerStatus.TxWarning; // 0x08
        if ((c & Libc.CAN_ERR_CRTL_RX_PASSIVE) != 0) cs |= CanControllerStatus.RxPassive; // 0x10
        if ((c & Libc.CAN_ERR_CRTL_TX_PASSIVE) != 0) cs |= CanControllerStatus.TxPassive; // 0x20
        if ((c & Libc.CAN_ERR_CRTL_ACTIVE) != 0) cs |= CanControllerStatus.Active; // 0x40

        return cs;
    }


    public static CanProtocolViolationType ToProtocolViolationType(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3) return CanProtocolViolationType.None;
        byte p = data[2];
        CanProtocolViolationType t = CanProtocolViolationType.None;

        if ((p & Libc.CAN_ERR_PROT_BIT) != 0) t |= CanProtocolViolationType.Bit; // 0x01
        if ((p & Libc.CAN_ERR_PROT_FORM) != 0) t |= CanProtocolViolationType.Form; // 0x02
        if ((p & Libc.CAN_ERR_PROT_STUFF) != 0) t |= CanProtocolViolationType.Stuff; // 0x04
        if ((p & Libc.CAN_ERR_PROT_BIT0) != 0) t |= CanProtocolViolationType.Bit0; // 0x08
        if ((p & Libc.CAN_ERR_PROT_BIT1) != 0) t |= CanProtocolViolationType.Bit1; // 0x10
        if ((p & Libc.CAN_ERR_PROT_OVERLOAD) != 0) t |= CanProtocolViolationType.Overload; // 0x20
        if ((p & Libc.CAN_ERR_PROT_ACTIVE) != 0) t |= CanProtocolViolationType.Active; // 0x40
        if ((p & Libc.CAN_ERR_PROT_TX) != 0) t |= CanProtocolViolationType.Tx; // 0x80

        return t;
    }

    public static FrameErrorLocation ToErrorLocation(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) return FrameErrorLocation.Unspecified;
        byte loc = data[3];

        return loc switch
        {
            // 0x00 未指定
            0x00 => FrameErrorLocation.Unspecified,
            // SOF
            0x03 => FrameErrorLocation.StartOfFrame,
            // Identifier
            0x02 or 0x06 or 0x07 or 0x0F or 0x0E => FrameErrorLocation.Identifier, // ID28_21 / ID20_18 / ID17_13 / ID12_05 / ID04_00
            0x04 => FrameErrorLocation.SRTR,
            0x05 => FrameErrorLocation.IDE,
            0x0C => FrameErrorLocation.RTR,
            0x0B => FrameErrorLocation.DLC,
            0x0A => FrameErrorLocation.DataField, // DATA
            // CRC
            0x08 => FrameErrorLocation.CRCSequence,
            0x18 => FrameErrorLocation.CRCDelimiter,
            // ACK
            0x19 => FrameErrorLocation.AckSlot,
            0x1B => FrameErrorLocation.AckDelimiter,
            // EOF
            0x1A => FrameErrorLocation.EndOfFrame,
            // Intermission
            0x12 => FrameErrorLocation.Intermission,

            0x9 or 0xD => FrameErrorLocation.Reserved, // Reserved

            < 32 => FrameErrorLocation.Unrecognized,

            _ => FrameErrorLocation.Invalid
        };
    }


    public static CanTransceiverStatus ToTransceiverStatus(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5) return CanTransceiverStatus.Unspecified;
        return data[4] switch
        {
            // 这些值与规范完全一致
            0x00 => CanTransceiverStatus.Unspecified, // CAN_ERR_TRX_UNSPEC
            0x04 => CanTransceiverStatus.CanHNoWire,
            0x05 => CanTransceiverStatus.CanHShortToBat,
            0x06 => CanTransceiverStatus.CanHShortToVcc,
            0x07 => CanTransceiverStatus.CanHShortToGnd,
            0x40 => CanTransceiverStatus.CanLNoWire,
            0x50 => CanTransceiverStatus.CanLShortToBat,
            0x60 => CanTransceiverStatus.CanLShortToVcc,
            0x70 => CanTransceiverStatus.CanLShortToGnd,
            0x80 => CanTransceiverStatus.CanLShortToCanH,
            _ => CanTransceiverStatus.Unknown
        };
    }

    public static FrameDirection InferFrameDirection(uint errMask, ReadOnlySpan<byte> data)
    {
        // 明确属于发送侧的错误
        if ((errMask & Libc.CAN_ERR_TX_TIMEOUT) != 0) return FrameDirection.Tx;     // 发送超时
        if ((errMask & Libc.CAN_ERR_ACK) != 0) return FrameDirection.Tx;     // 缺少 ACK 只可能发生在发送端
        if ((errMask & Libc.CAN_ERR_LOSTARB) != 0) return FrameDirection.Tx;     // 仲裁丢失在发送竞争时出现

        // 协议违规：data[2] 的 PROT_TX 位表示该违规发生在发送路径
        if ((errMask & Libc.CAN_ERR_PROT) != 0 && data.Length >= 3)
        {
            byte p = data[2];
            if ((p & Libc.CAN_ERR_PROT_TX) != 0) return FrameDirection.Tx;
            // 其他协议错误（BIT/FORM/Stuff...）未携带方向，先不武断归类
        }

        // 控制器状态：仅出现某一侧（Rx 或 Tx）的状态位时作为弱信号
        if ((errMask & Libc.CAN_ERR_CRTL) != 0 && data.Length >= 2)
        {
            byte c = data[1];

            bool rxOnly =
                ((c & (Libc.CAN_ERR_CRTL_RX_OVERFLOW | Libc.CAN_ERR_CRTL_RX_WARNING | Libc.CAN_ERR_CRTL_RX_PASSIVE)) != 0)
                && ((c & (Libc.CAN_ERR_CRTL_TX_OVERFLOW | Libc.CAN_ERR_CRTL_TX_WARNING | Libc.CAN_ERR_CRTL_TX_PASSIVE)) == 0);

            bool txOnly =
                ((c & (Libc.CAN_ERR_CRTL_TX_OVERFLOW | Libc.CAN_ERR_CRTL_TX_WARNING | Libc.CAN_ERR_CRTL_TX_PASSIVE)) != 0)
                && ((c & (Libc.CAN_ERR_CRTL_RX_OVERFLOW | Libc.CAN_ERR_CRTL_RX_WARNING | Libc.CAN_ERR_CRTL_RX_PASSIVE)) == 0);

            if (txOnly) return FrameDirection.Tx;
            if (rxOnly) return FrameDirection.Rx;
        }

        // 4) 其余情况方向不确定
        return FrameDirection.Unknown;
    }

    public static CanErrorCounters? ToErrorCounters(uint errMask, ReadOnlySpan<byte> data)
    {
        if ((errMask & Libc.CAN_ERR_CNT) == 0 || data.Length < 8)
            return null;
        return new CanErrorCounters()
        {
            ReceiveErrorCounter = data[7],
            TransmitErrorCounter = data[6]
        };
    }

    public static byte? ToArbitrationLostBit(uint errMask, ReadOnlySpan<byte> data)
    {
        if ((errMask & Libc.CAN_ERR_LOSTARB) == 0 || data.Length < 6)
            return null;
        var pos = data[5];
        return pos < 32 ? pos : null;
    }


}
