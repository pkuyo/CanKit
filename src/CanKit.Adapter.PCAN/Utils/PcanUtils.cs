using CanKit.Adapter.PCAN.Exceptions;
using CanKit.Core.Definitions;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN;

public static class PcanUtils
{
    public static CanProtocolViolationType ToProtocolViolationType(int id, ReadOnlySpan<byte> data)
    {
        var t = CanProtocolViolationType.None;

        // ID（ERRC1:ERRC0）→ 类型
        if ((id & 0x1) != 0) t |= CanProtocolViolationType.Bit;   // 1
        if ((id & 0x2) != 0) t |= CanProtocolViolationType.Form;  // 2
        if ((id & 0x4) != 0) t |= CanProtocolViolationType.Stuff; // 4
        // 8 = Other（ACK/CRC/…），无法细分到 Bit0/Bit1；留给“位置”去提示

        // DATA[0]（DIR）
        var dir = data[0];
        if (dir == 0) t |= CanProtocolViolationType.Tx;


        // DATA[1]（位置码）
        byte loc = data[1];
        if (loc == 17) t |= CanProtocolViolationType.Active;   // Active Error Flag
        if (loc == 28) t |= CanProtocolViolationType.Overload; // Overload Flag


        return t;
    }

#pragma warning disable IDE0055
    public static FrameErrorLocation ToErrorLocation(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return FrameErrorLocation.Invalid;
        byte seg = data[1];

        return seg switch
        {
            0   => FrameErrorLocation.Unspecified,
            3   => FrameErrorLocation.StartOfFrame,
            2 or 6 or 7 or 14 or 15 => FrameErrorLocation.Identifier,
            4   => FrameErrorLocation.SRTR,
            5   => FrameErrorLocation.IDE,
            12  => FrameErrorLocation.RTR,
            11  => FrameErrorLocation.DLC,
            10  => FrameErrorLocation.DataField,
            8   => FrameErrorLocation.CRCSequence,
            24  => FrameErrorLocation.CRCDelimiter,
            25  => FrameErrorLocation.AckSlot,
            27  => FrameErrorLocation.AckDelimiter,
            26  => FrameErrorLocation.EndOfFrame,
            18  => FrameErrorLocation.Intermission,
            17  => FrameErrorLocation.ActiveErrorFlag,
            22  => FrameErrorLocation.PassiveErrorFlag,
            23  => FrameErrorLocation.ErrorDelimiter,
            28  => FrameErrorLocation.OverloadFlag,
            9 or 13 => FrameErrorLocation.Reserved,      // Reserved
            19  => FrameErrorLocation.TolerateDominantBits,
            <32 => FrameErrorLocation.Unrecognized,
            _   => FrameErrorLocation.Invalid
        };
    }
    #pragma warning restore IDE0055

    public static CanTransceiverStatus ToTransceiverStatus(ReadOnlySpan<byte> data)
        => CanTransceiverStatus.Unknown;


    public static FrameDirection ToDirection(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1) return FrameDirection.Unknown;
        return data[0] == 0 ? FrameDirection.Tx : FrameDirection.Rx;
    }


    public static FrameErrorType ToFrameErrorType(int id, ReadOnlySpan<byte> data)
    {
        var t = FrameErrorType.Unknown;

        // ECC 类型 都属于“协议违规”大类
        if ((id & 0xF) != 0) t = FrameErrorType.ProtocolViolation;

        // 位置提示：ACK Slot/Delimiter → AckError

        byte loc = data[1];
        if (loc == 25 || loc == 27) t |= FrameErrorType.AckError;
        if (loc == 28) t |= FrameErrorType.ProtocolViolation; // Overload 也归入协议类



        int rec = data[2], tec = data[3];
        if (rec >= 96 || tec >= 96) t |= FrameErrorType.Controller;

        return t;
    }

    public static CanErrorCounters ToErrorCounters(ReadOnlySpan<byte> data)
    {
        return new CanErrorCounters()
        {
            TransmitErrorCounter = data[2],
            ReceiveErrorCounter = data[3]
        };
    }

    public static void ThrowIfError(PcanStatus status, string operation, string message)
    {
        if (status != PcanStatus.OK)
            throw new PcanCanException(operation, message, status);
    }
}
