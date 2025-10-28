using System.Buffers;
using System.Runtime.CompilerServices;
using CanKit.Core.Definitions;
using CanKit.Protocol.IsoTp.Defines;

namespace CanKit.Protocol.IsoTp.Utils;

internal static partial class FrameCodec
{

    internal static bool TryParsePci(in CanReceiveData rx, in IsoTpEndpoint ep, out Pci pci)
    {

        var data = rx.CanFrame.Data.Span;
        var pciStart = (ep.IsExtendedAddress ? 1 : 0);
        var typeNum = data[pciStart] >> 4;
        var fd = rx.CanFrame.FrameKind == CanFrameType.CanFd;
        if (typeNum >= 4 || rx.CanFrame.ID != ep.RxId || (ep.SourceAddress != null && ep.SourceAddress != data[0]))
        {
            pci = default;
            return false;
        }
        var type = (PciType)typeNum;
        switch (type)
        {
            case PciType.SF:
                if ((data[pciStart] & 0xF) == 0)
                {
                    if (fd)
                    {
                        pci = new Pci(type, data[pciStart + 1], 0, default, 0, TimeSpan.Zero);
                    }
                    else
                    {
                        pci = default;
                        return false;
                        //TODO:错误数据处理
                    }
                }
                else
                {
                    pci = new Pci(type, data[pciStart] & 0xF, 0, default, 0, TimeSpan.Zero);
                }
                break;
            case PciType.FF:
                if ((data[pciStart] & 0xF) == 0 && data[pciStart + 1] == 0)
                {
                    if (fd)
                    {
                        pci = new Pci(type,
                            (data[pciStart + 2] << 24) | (data[pciStart + 3] << 16) | (data[pciStart + 4] << 8) | data[pciStart + 5],
                            0, default, 0, TimeSpan.Zero);
                    }
                    else
                    {
                        pci = default;
                        return false;
                        //TODO:错误数据处理
                    }
                }
                else
                {
                    pci = new Pci(type, (data[pciStart] & 0xF << 8) | data[pciStart + 1], 0, default, 0, TimeSpan.Zero);
                }
                break;
            case PciType.FC:
                var fs = data[pciStart] & 0xF;
                if (fs > 3)
                {
                    pci = default;
                    return false;
                    //TODO:错误数据处理
                }
                pci = new Pci(type, 0, 0, (FlowStatus)fs, data[pciStart + 1], DecodeStmin(data[pciStart + 2]));
                break;
            case PciType.CF:
            default:
                pci = new Pci(type, 0, (byte)(data[pciStart] & 0xF), default, 0, TimeSpan.Zero);
                break;

        }
        return true;
    }

    internal static unsafe PoolFrame BuildSF(in IsoTpEndpoint ep, ReadOnlySpan<byte> payload, bool padding, bool canfd)
    {
        var pciStart = (ep.IsExtendedAddress ? 1 : 0);
        var len = payload.Length + 2 + pciStart;
        if (padding)
        {
            len = canfd ? 64 : 8;
        }
        var data = ArrayPool<byte>.Shared.Rent(len);
        if (ep.IsExtendedAddress)
        {
            data[0] = ep.TargetAddress!.Value;
        }

        if (canfd && payload.Length + pciStart > 7)
        {
            data[pciStart++] = (byte)PciType.SF << 4;
            data[pciStart] = (byte)payload.Length;
        }
        else
        {
            data[pciStart] = (byte)(((byte)PciType.SF << 4) | (payload.Length & 0xF));
        }
        fixed (byte* src = payload)
        fixed (byte* dst = data)
        {
            Unsafe.CopyBlockUnaligned(dst + pciStart + 1, src, (uint)payload.Length);
            if (padding)
            {
                Unsafe.InitBlockUnaligned(dst + pciStart + 1 + (uint)payload.Length, 0,
                    (uint)(len - pciStart - 1 - payload.Length));
            }
        }
        return new PoolFrame(canfd
            ? new CanClassicFrame(ep.TxId, data, ep.IsExtendedId)
            : new CanFdFrame(ep.TxId, data, ep.IsExtendedId), data);
    }

    internal static unsafe PoolFrame BuildFF(in IsoTpEndpoint ep,
        int totalLen,
        ReadOnlySpan<byte> firstChunk,
        bool canfd)
    {
        var len = canfd ? 64 : 8;
        var pciStart = (ep.IsExtendedAddress ? 1 : 0);
        var data = ArrayPool<byte>.Shared.Rent(len);
        if (ep.IsExtendedAddress)
        {
            data[0] = ep.TargetAddress!.Value;
        }

        if (firstChunk.Length > 4095)
        {
            if (!canfd)
            {
                throw new Exception(); //TODO:异常处理
            }
            var dataLen = firstChunk.Length;
            data[pciStart] = (byte)PciType.FF << 4;
            data[pciStart + 1] = 0;
            data[pciStart + 2] = (byte)((dataLen >> 24) & 0xFF);
            data[pciStart + 3] = (byte)((dataLen >> 16) & 0xFF);
            data[pciStart + 4] = (byte)((dataLen >> 8) & 0xFF);
            data[pciStart + 5] = (byte)(dataLen & 0xFF);
            pciStart += 4;
        }
        else
        {
            data[pciStart] = (byte)(((byte)PciType.FF << 4) | (totalLen >> 8));
            data[pciStart+1] = (byte)(totalLen & 0xFF);
        }

        fixed (byte* src = firstChunk)
        fixed (byte* dst = data)
        {
            Unsafe.CopyBlockUnaligned(dst + pciStart + 2, src, (uint)(len - pciStart - 2));
        }

        return new PoolFrame(canfd
            ? new CanClassicFrame(ep.TxId, data, ep.IsExtendedId)
            : new CanFdFrame(ep.TxId, data, ep.IsExtendedId), data);
    }

    internal static unsafe PoolFrame BuildCF(in IsoTpEndpoint ep, byte sn, ReadOnlySpan<byte> chunk, bool padding, bool canfd)
    {
        var pciStart = (ep.IsExtendedAddress ? 1 : 0);
        var len = chunk.Length + 2 + pciStart;
        if (padding)
        {
            len = canfd ? NextFdLen(len) : 8;
        }
        var data = ArrayPool<byte>.Shared.Rent(len);
        if (ep.IsExtendedAddress)
        {
            data[0] = ep.TargetAddress!.Value;
        }
        data[pciStart] = (byte)(((byte)PciType.CF << 4) | (sn & 0xF));
        fixed (byte* src = chunk)
        fixed (byte* dst = data)
        {
            Unsafe.CopyBlockUnaligned(dst + pciStart + 1, src, (uint)chunk.Length);
            if (padding)
            {
                Unsafe.InitBlockUnaligned(dst + pciStart + 1 + (uint)chunk.Length, 0,
                    (uint)(len - pciStart - 1 - chunk.Length));
            }
        }
        return new PoolFrame(canfd
            ? new CanClassicFrame(ep.TxId, data, ep.IsExtendedId)
            : new CanFdFrame(ep.TxId, data, ep.IsExtendedId), data);
    }

    internal static unsafe PoolFrame BuildFC(in IsoTpEndpoint ep, FlowStatus fs, byte bs, byte stmin, bool padding, bool canfd)
    {
        var pciStart = (ep.IsExtendedAddress ? 1 : 0);
        var len = padding ? 8 : pciStart + 2;
        var data = ArrayPool<byte>.Shared.Rent(len);
        if (ep.IsExtendedAddress)
        {
            data[0] = ep.TargetAddress!.Value;
        }
        data[pciStart] = (byte)(((byte)PciType.FF << 4) | ((byte)fs & 0xF));
        data[pciStart + 1] = bs;
        data[pciStart + 2] = stmin;
        if (padding)
        {
            fixed (byte* dst = data)
            {

                Unsafe.InitBlockUnaligned(dst + pciStart + 1, 0,
                        (uint)(len - pciStart - 1));
            }
        }
        return new PoolFrame(canfd
            ? new CanClassicFrame(ep.TxId, data, ep.IsExtendedId)
            : new CanFdFrame(ep.TxId, data, ep.IsExtendedId), data);
    }

    internal static byte EncodeStmin(TimeSpan st)
    {
        var micro = st.Ticks / 10;
        return micro switch
        {
            >= 100 and < 1000 => (byte)((micro / 100) + 0xF0),
            > 1000 and < 128_000 => (byte)(micro / 1000),
            _ => throw new Exception() //TODO:异常处理
        };
    }

    internal static TimeSpan DecodeStmin(byte raw)
    {
        return raw switch
        {
            <= 0x7F => TimeSpan.FromMilliseconds(raw),
            >= 0xF1 and <= 0xF9 => TimeSpan.FromTicks((raw-0xF0) * 1000),
            _ => throw new Exception() //TODO:异常处理
        };
    }
}

internal static partial class FrameCodec
{
    private static int NextFdLen(int n)
    {
        return n switch
        {
            <= 8 => 8,
            <= 12 => 12,
            <= 16 => 16,
            <= 24 => 24,
            <= 32 => 32,
            <= 48 => 48,
            _ => 64
        };
    }
}

// PCI 解码后的结构体
internal readonly record struct Pci(PciType Type, int Len, byte SN, FlowStatus FS, byte BS, TimeSpan STmin);
