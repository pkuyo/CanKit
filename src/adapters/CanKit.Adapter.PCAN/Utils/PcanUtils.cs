using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.PCAN.Exceptions;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;
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

    public static Bitrate MapClassicBaud(CanBusTiming timing)
    {
        if (!timing.Classic!.Value.Nominal.IsTarget)
        {
            throw new CanBusConfigurationException(
                "Classic timing must specify a target nominal bitrate.");
        }

        var b = timing.Classic.Value.Nominal.Bitrate!.Value;
        return b switch
        {
            1_000_000 => Bitrate.Pcan1000,
            800_000 => Bitrate.Pcan800,
            500_000 => Bitrate.Pcan500,
            250_000 => Bitrate.Pcan250,
            125_000 => Bitrate.Pcan125,
            100_000 => Bitrate.Pcan100,
            83_000 => Bitrate.Pcan83,
            95_000 => Bitrate.Pcan95,
            50_000 => Bitrate.Pcan50,
            47_000 => Bitrate.Pcan47,
            33_000 => Bitrate.Pcan33,
            20_000 => Bitrate.Pcan20,
            10_000 => Bitrate.Pcan10,
            5_000 => Bitrate.Pcan5,
            _ => throw new CanBusConfigurationException($"Unsupported PCAN classic bitrate: {b}")
        };
    }

    public static BitrateFD MapFdBitrate(CanBusTiming timing)
    {

        var (nomial, data, clockTmp) = timing.Fd!.Value;
        var clock = clockTmp ?? 80;
        // If advanced override is provided, build a custom FD string using same segments for both phases.
        BitrateFD.BitrateSegment nominalSeg = new BitrateFD.BitrateSegment();
        BitrateFD.BitrateSegment dataSeg = new BitrateFD.BitrateSegment();

        if (!Enum.IsDefined(typeof(BitrateFD.ClockFrequency), clock * 1_000_000))
        {
            throw new CanBusConfigurationException(
                $"Unsupported PCAN FD clock frequency: {clock} MHz.");
        }

        if (nomial.Segments is { } seg)
        {
            nominalSeg.Tseg1 = seg.Tseg1;
            nominalSeg.Tseg2 = seg.Tseg2;
            nominalSeg.Brp = seg.Brp;
            nominalSeg.Mode = BitrateFD.BitrateType.ArbitrationPhase;
            nominalSeg.Sjw = seg.Sjw;

        }
        else
        {
            var bit = timing.Fd.Value.Nominal.Bitrate!.Value;
            var samplePoint = timing.Fd.Value.Nominal.SamplePointPermille ?? 800;
            var segment = BitTimingSolver.FromSamplePoint(clock, bit, samplePoint/1000.0);
            nominalSeg.Tseg1 = segment.Tseg1;
            nominalSeg.Tseg2 = segment.Tseg2;
            nominalSeg.Brp = segment.Brp;
            nominalSeg.Mode = BitrateFD.BitrateType.ArbitrationPhase;
            nominalSeg.Sjw = segment.Sjw;
        }

        if (data.Segments is { } seg1)
        {
            dataSeg.Tseg1 = seg1.Tseg1;
            dataSeg.Tseg2 = seg1.Tseg2;
            dataSeg.Brp = seg1.Brp;
            dataSeg.Mode = BitrateFD.BitrateType.DataPhase;
            dataSeg.Sjw = seg1.Sjw;
        }
        else
        {
            var bit = timing.Fd.Value.Nominal.Bitrate!.Value;
            var samplePoint = timing.Fd.Value.Nominal.SamplePointPermille ?? 800;
            var segment = BitTimingSolver.FromSamplePoint(clock, bit, samplePoint/1000.0);
            dataSeg.Tseg1 = segment.Tseg1;
            dataSeg.Tseg2 = segment.Tseg2;
            dataSeg.Brp = segment.Brp;
            dataSeg.Mode = BitrateFD.BitrateType.ArbitrationPhase;
            dataSeg.Sjw = segment.Sjw;
        }
        return new BitrateFD((BitrateFD.ClockFrequency)(clock * 1_000_000), nominalSeg, dataSeg);
    }

    public static PcanChannel PcanHandle(this in BusNativeHandle handle) => (PcanChannel)handle.HandleValue;
}
