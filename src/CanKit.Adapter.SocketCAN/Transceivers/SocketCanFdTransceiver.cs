using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.SocketCAN.Native;
using CanKit.Adapter.SocketCAN.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.SocketCAN.Transceivers;



public sealed class SocketCanFdTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<ICanFrame> frames, int _ = 0)
    {
        var ch = (SocketCanBus)channel;
        var totalSent = 0;
        var index = 0;
        unsafe
        {
            var sizeClassic = Marshal.SizeOf<Libc.can_frame>();
            var sizeFd = Marshal.SizeOf<Libc.canfd_frame>();
            Libc.can_frame* cfBuf = stackalloc Libc.can_frame[Libc.BATCH_COUNT];
            Libc.canfd_frame* fdBuf = stackalloc Libc.canfd_frame[Libc.BATCH_COUNT];
            Libc.iovec* iov = stackalloc Libc.iovec[Libc.BATCH_COUNT];
            Libc.mmsghdr* msgs = stackalloc Libc.mmsghdr[Libc.BATCH_COUNT];
            foreach (var f in frames)
            {
                if (index == Libc.BATCH_COUNT)
                {
                    int sent;
                    do
                    {
                        sent = Libc.sendmmsg(ch.FileDescriptor, msgs, 64, 0);
                    } while (sent < 0 && Libc.Errno() == Libc.EINTR);
                    if (sent < 0)
                    {
                        var errno = Libc.Errno();
                        if (errno == Libc.EAGAIN) return (uint)totalSent;
                        Libc.ThrowErrno("sendmmsg(FD)", "Failed to send classic CAN frames");
                    }
                    totalSent += sent;
                    index = 0;
                    if (sent != Libc.BATCH_COUNT)
                        break;
                }
                if (f is CanFdFrame fdf)
                {
                    fdBuf[index] = fdf.ToCanFrame();
                    iov[index].iov_base = &fdBuf[index];
                    iov[index].iov_len = (UIntPtr)sizeFd;
                }
                else if (f is CanClassicFrame ccf)
                {
                    cfBuf[index] = ccf.ToCanFrame();
                    iov[index].iov_base = &cfBuf[index];
                    iov[index].iov_len = (UIntPtr)sizeClassic;
                }
                else
                {
                    throw new InvalidOperationException(
                        "SocketCanFdTransceiver requires CanClassicFrame/CanFdFrame");
                }

                msgs[index].msg_hdr = new Libc.msghdr
                {
                    msg_name = null,
                    msg_namelen = 0,
                    msg_iov = &iov[index],
                    msg_iovlen = (UIntPtr)1,
                    msg_control = null,
                    msg_controllen = UIntPtr.Zero,
                    msg_flags = 0
                };
                msgs[index].msg_len = 0;
                index++;
            }
            int s;
            do
            {
                s = Libc.sendmmsg(ch.FileDescriptor, msgs, (uint)index, 0);
            } while (s < 0 && Libc.Errno() == Libc.EINTR);
            if (s < 0)
            {
                var errno = Libc.Errno();
                if (errno == Libc.EAGAIN) return (uint)totalSent;
                Libc.ThrowErrno("sendmmsg(FD)", "Failed to send classic CAN frames");
            }
            totalSent += s;

            return (uint)totalSent;
        }
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, uint count = 1, int _ = -1)
    {
        var result = new List<CanReceiveData>();
        var ch = (SocketCanBus)bus;
        var preferTs = ch.Options.PreferKernelTimestamp;

        bool inf = count == 0;
        unsafe
        {
            var sizeClassic = Marshal.SizeOf<Libc.can_frame>();
            var sizeFd = Marshal.SizeOf<Libc.canfd_frame>();
            Libc.canfd_frame* fdBuf = stackalloc Libc.canfd_frame[Libc.BATCH_COUNT];
            Libc.iovec* iov = stackalloc Libc.iovec[Libc.BATCH_COUNT];
            Libc.mmsghdr* msgs = stackalloc Libc.mmsghdr[Libc.BATCH_COUNT];
            byte* cbase = stackalloc byte[Libc.BATCH_COUNT * 256];
            while (inf || count > 0)
            {
                var oneBatch = (int)Math.Max(1, Math.Min(count == 0 ? Libc.BATCH_COUNT : count, Libc.BATCH_COUNT));
                for (int i = 0; i < oneBatch; i++)
                {
                    iov[i].iov_base = &fdBuf[i];
                    iov[i].iov_len = (UIntPtr)sizeFd;
                    msgs[i].msg_hdr = new Libc.msghdr
                    {
                        msg_name = null,
                        msg_namelen = 0,
                        msg_iov = &iov[i],
                        msg_iovlen = (UIntPtr)1,
                        msg_control = preferTs ? (cbase + (i * 256)) : null,
                        msg_controllen = preferTs ? (UIntPtr)256 : UIntPtr.Zero,
                        msg_flags = 0
                    };
                    msgs[i].msg_len = 0;
                }

                int recvd;
                do
                {
                    recvd = Libc.recvmmsg(ch.FileDescriptor, msgs, (uint)oneBatch, 0, null);
                }
                while (recvd < 0 && Libc.Errno() == Libc.EINTR);


                if (recvd < 0)
                {
                    var errno = Libc.Errno();
                    if (errno == Libc.EAGAIN) return result;
                    Libc.ThrowErrno("recvmmsg(FD)", "Failed to read CAN frames");
                }
                count -= (uint)recvd;
                for (int i = 0; i < recvd; i++)
                {
                    var msg = msgs[i].msg_hdr;
                    TimeSpan tsSpan = preferTs ? ExtractTimestamp(ref msg) : TimeSpan.Zero;
                    BuildFromFdOrClassic(&fdBuf[i], (int)msgs[i].msg_len, tsSpan, result, sizeClassic, sizeFd);
                }
            }
        }
        return result;
    }

    private static unsafe void BuildFromFdOrClassic(Libc.canfd_frame* buf, int n, TimeSpan tsSpan, List<CanReceiveData> acc, int sizeClassic, int sizeFd)
    {
        if (n <= 0) return;
        if (tsSpan == TimeSpan.Zero)
        {
            var now = DateTimeOffset.UtcNow;
            tsSpan = now - _epoch;
        }

        if (n == sizeFd)
        {
            int dataLen = buf->len;
            var data = dataLen == 0 ? Array.Empty<byte>() : new byte[dataLen];
            fixed (byte* pData = data)
            {
                Buffer.MemoryCopy(buf->data, pData, data.Length, Math.Min(dataLen, 64));
            }
            bool brs = (buf->flags & Libc.CANFD_BRS) != 0;
            bool esi = (buf->flags & Libc.CANFD_ESI) != 0;
            bool err = (buf->flags & Libc.CAN_ERR_FLAG) != 0;
            acc.Add(new CanReceiveData(new CanFdFrame((buf->can_id & Libc.CAN_EFF_MASK) == 1 ?
                    buf->can_id & Libc.CAN_EFF_MASK :
                    buf->can_id & Libc.CAN_SFF_MASK, data, brs, esi)
            { IsErrorFrame = err })
            { ReceiveTimestamp = tsSpan });
        }
        else if (n == sizeClassic)
        {
            var cf = (Libc.can_frame*)buf;
            int dataLen = cf->can_dlc;
            var data2 = dataLen == 0 ? Array.Empty<byte>() : new byte[dataLen];
            fixed (byte* pData = data2)
            {
                Buffer.MemoryCopy(cf->data, pData, data2.Length, Math.Min(dataLen, 8));
            }
            bool err = (cf->can_id & Libc.CAN_ERR_FLAG) != 0;
            acc.Add(new CanReceiveData(new CanClassicFrame((cf->can_id & Libc.CAN_EFF_MASK) == 1 ?
                    cf->can_id & Libc.CAN_EFF_MASK :
                    cf->can_id & Libc.CAN_SFF_MASK, data2)
            { IsErrorFrame = err })
            { ReceiveTimestamp = tsSpan });
        }
    }

    private static unsafe TimeSpan ExtractTimestamp(ref Libc.msghdr msg)
    {
        if (msg.msg_control == null || msg.msg_controllen == UIntPtr.Zero) return TimeSpan.Zero;
        byte* c = (byte*)msg.msg_control;
        ulong clen = msg.msg_controllen.ToUInt64();
        byte* end = c + (long)clen;
        while (c + (ulong)Marshal.SizeOf<Libc.cmsghdr>() <= end)
        {
            var hdr = (Libc.cmsghdr*)c;
            ulong hlen = hdr->cmsg_len.ToUInt64();
            if (hlen < (ulong)Marshal.SizeOf<Libc.cmsghdr>()) break;
            byte* data = c + (ulong)Marshal.SizeOf<Libc.cmsghdr>();
            if (hdr->cmsg_level == Libc.SOL_SOCKET)
            {
                if (hdr->cmsg_type == Libc.SCM_TIMESTAMPING)
                {
                    var t = (Libc.timespec*)data;
                    var raw = t[2];
                    var sw = t[0];
                    var use = (raw.tv_sec != 0 || raw.tv_nsec != 0) ? raw : sw;
                    var dto = DateTimeOffset.FromUnixTimeSeconds(use.tv_sec).AddTicks(use.tv_nsec / 100);
                    return dto - _epoch;
                }
                else if (hdr->cmsg_type == Libc.SCM_TIMESTAMPNS)
                {
                    var t = *(Libc.timespec*)data;
                    var dto = DateTimeOffset.FromUnixTimeSeconds(t.tv_sec).AddTicks(t.tv_nsec / 100);
                    return dto - _epoch;
                }
            }
            ulong align = (ulong)IntPtr.Size;
            ulong step = ((hlen + align - 1) / align) * align;
            c += (long)step;
        }
        return TimeSpan.Zero;
    }

    private static readonly DateTimeOffset _epoch = new(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
}
