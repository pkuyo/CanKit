using CanKit.Adapter.SocketCAN.Native;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.SocketCAN.Utils;

internal static class SocketCanExtension
{

    public static LibSocketCan.can_bittiming ToCanBitTiming(this CanPhaseTiming timing, uint clock)
    {
        if (timing.IsTarget)
        {
            return new LibSocketCan.can_bittiming
            {
                bitrate = timing.Bitrate!.Value,
                sample_point = timing.SamplePointPermille ?? 0
            };
        }
        else
        {
            var seg = timing.Segments!.Value;
            return new LibSocketCan.can_bittiming
            {
                tq = (1_000 * seg.Brp) / clock,
                prop_seg = Math.Max(1, seg.Tseg1 / 2),
                phase_seg1 = seg.Tseg1 - Math.Max(1, seg.Tseg1 / 2),
                phase_seg2 = seg.Tseg2,
                sjw = seg.Sjw
            };
        }
    }

    public static Libc.can_frame ToCanFrame(this CanClassicFrame cf)
    {
        var frame = new Libc.can_frame
        {
            can_id = cf.RawID,
            can_dlc = cf.Dlc,
            __pad = 0,
            __res0 = 0,
            __res1 = 0,
        };
        unsafe
        {
            var src = cf.Data.Span;
            var copy = Math.Min(src.Length, 8);
            fixed (byte* pSrc = src)
            {
                Buffer.MemoryCopy(pSrc, frame.data, 8, copy);
            }
        }

        return frame;
    }

    public static Libc.canfd_frame ToCanFrame(this CanFdFrame ff)
    {
        var frame = new Libc.canfd_frame
        {
            can_id = ff.RawID,
            len = (byte)CanFdFrame.DlcToLen(ff.Dlc),
            flags = (byte)((ff.BitRateSwitch ? Libc.CANFD_BRS : 0) | (ff.ErrorStateIndicator ? Libc.CANFD_ESI : 0)),
            __res0 = 0,
            __res1 = 0,
        };

        unsafe
        {
            var src = ff.Data.Span;
            var copy = Math.Min(src.Length, 64);
            fixed (byte* pSrc = src)
            {
                Buffer.MemoryCopy(pSrc, frame.data, 64, copy);
            }
        }
        return frame;
    }
}
