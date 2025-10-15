using System.Runtime.CompilerServices;
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
            can_id = cf.ToCanID(),
            can_dlc = cf.Dlc,
            __pad = 0,
            __res0 = 0,
            __res1 = 0,
        };
        unsafe
        {
            var src = cf.Data.Span;
            var copy = (uint)Math.Min(src.Length, 8);
            fixed (byte* pSrc = src)
            {
                Unsafe.CopyBlockUnaligned(frame.data, pSrc, copy);
            }
        }

        return frame;
    }

    public static Libc.canfd_frame ToCanFrame(this CanFdFrame ff)
    {
        var frame = new Libc.canfd_frame
        {
            can_id = ff.ToCanID(),
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
                Unsafe.CopyBlockUnaligned(frame.data, pSrc, (uint)copy);
            }
        }
        return frame;
    }

    public static uint ToCanID(this CanClassicFrame frame)
    {
        var id = (uint)frame.ID;
        var cid = frame.IsExtendedFrame ? ((id & Libc.CAN_EFF_MASK) | Libc.CAN_EFF_FLAG)
            :  (id & Libc.CAN_SFF_MASK);
        if (frame.IsRemoteFrame)      cid |= Libc.CAN_RTR_FLAG;
        if (frame.IsErrorFrame)      cid |= Libc.CAN_ERR_FLAG;
        return cid;
    }

    public static uint ToCanID(this ICanFrame frame)
    {
        var id = (uint)frame.ID;
        var cid = frame.IsExtendedFrame ? ((id & Libc.CAN_EFF_MASK) | Libc.CAN_EFF_FLAG)
            :  (id & Libc.CAN_SFF_MASK);
        if (frame.IsErrorFrame)      cid |= Libc.CAN_ERR_FLAG;
        return cid;
    }

    public static Libc.timeval ToTimeval(TimeSpan t)
    {
        return new Libc.timeval
        {
            tv_sec = (int)Math.Floor(t.TotalSeconds),
            tv_usec = (int)((t - TimeSpan.FromSeconds(Math.Floor(t.TotalSeconds))).TotalMilliseconds * 1000.0)
        };
    }
}
