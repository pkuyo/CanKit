using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.SocketCAN.Native;

namespace Pkuyo.CanKit.Net.SocketCAN;

internal static class SocketCanErrors
{
    public static FrameErrorKind MapToKind(uint errMask, ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        var result = FrameErrorKind.None;
        // Direct class mapping
        if ((errMask & Libc.CAN_ERR_TX_TIMEOUT) != 0)
            result |= FrameErrorKind.TxTimeout;
        if ((errMask & Libc.CAN_ERR_LOSTARB) != 0)
            result |= FrameErrorKind.ArbitrationLost;
        if ((errMask & Libc.CAN_ERR_ACK) != 0)
            result |= FrameErrorKind.AckError;
        if ((errMask & Libc.CAN_ERR_BUSOFF) != 0)
            result |= FrameErrorKind.BusOff;
        if ((errMask & Libc.CAN_ERR_RESTARTED) != 0)
            result |= FrameErrorKind.Restarted;
        if ((errMask & Libc.CAN_ERR_BUSERROR) != 0)
            result |= FrameErrorKind.BusError;
        if ((errMask & Libc.CAN_ERR_TRX) != 0)
            result |= FrameErrorKind.TransceiverError;

        // Controller state details in data[1]
        if ((errMask & Libc.CAN_ERR_CRTL) != 0)
        {
            byte ctrl = span.Length > 1 ? span[1] : (byte)0;
            if ((ctrl & Libc.CAN_ERR_CRTL_RX_OVERFLOW) != 0)
                result |= FrameErrorKind.RxOverflow;
            if ((ctrl & Libc.CAN_ERR_CRTL_TX_OVERFLOW) != 0)
                result |= FrameErrorKind.TxOverflow;
            if ((ctrl & (Libc.CAN_ERR_CRTL_RX_WARNING | Libc.CAN_ERR_CRTL_TX_WARNING)) != 0)
                result |= FrameErrorKind.Warning;
            if ((ctrl & (Libc.CAN_ERR_CRTL_RX_PASSIVE | Libc.CAN_ERR_CRTL_TX_PASSIVE)) != 0)
                result |= FrameErrorKind.Passive;
            result |= FrameErrorKind.Controller;
        }

        // Protocol violations types in data[2]
        if ((errMask & Libc.CAN_ERR_PROT) != 0)
        {
            byte prot = span.Length > 2 ? span[2] : (byte)0;
            if ((prot & Libc.CAN_ERR_PROT_STUFF) != 0)
                result |= FrameErrorKind.StuffError;
            if ((prot & Libc.CAN_ERR_PROT_FORM) != 0)
                result |= FrameErrorKind.FormError;
            if ((prot & (Libc.CAN_ERR_PROT_BIT | Libc.CAN_ERR_PROT_BIT0 | Libc.CAN_ERR_PROT_BIT1)) != 0)
                result |= FrameErrorKind.BitError;
            if ((prot & Libc.CAN_ERR_PROT_OVERLOAD) != 0)
                result |= FrameErrorKind.Overload;
            result |= FrameErrorKind.Controller;
        }

        return result == FrameErrorKind.None ? FrameErrorKind.Unknown : result;
    }
}
