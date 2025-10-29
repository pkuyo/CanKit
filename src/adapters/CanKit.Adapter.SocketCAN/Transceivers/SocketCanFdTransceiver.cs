using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CanKit.Adapter.SocketCAN.Native;
using CanKit.Adapter.SocketCAN.Definitions;
using CanKit.Adapter.SocketCAN.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.SocketCAN.Transceivers;



public sealed class SocketCanFdTransceiver : ITransceiver
{
    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<ICanFrame> frames)
    {
        var ch = (SocketCanBus)bus;
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
                        sent = Libc.sendmmsg(ch.Handle, msgs, Libc.BATCH_COUNT, 0);
                    } while (sent < 0 && Libc.Errno() == Libc.EINTR);
                    if (sent < 0)
                    {
                        var errno = Libc.Errno();
                        if (errno == Libc.EAGAIN) return totalSent;
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
                s = Libc.sendmmsg(ch.Handle, msgs, (uint)index, 0);
            } while (s < 0 && Libc.Errno() == Libc.EINTR);
            if (s < 0)
            {
                var errno = Libc.Errno();
                if (errno == Libc.EAGAIN) return totalSent;
                Libc.ThrowErrno("sendmmsg(FD)", "Failed to send classic CAN frames");
            }
            totalSent += s;

            return totalSent;
        }
    }

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ReadOnlySpan<ICanFrame> frames)
    {
        var ch = (SocketCanBus)bus;
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
                        sent = Libc.sendmmsg(ch.Handle, msgs, Libc.BATCH_COUNT, 0);
                    } while (sent < 0 && Libc.Errno() == Libc.EINTR);
                    if (sent < 0)
                    {
                        var errno = Libc.Errno();
                        if (errno == Libc.EAGAIN) return totalSent;
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
                s = Libc.sendmmsg(ch.Handle, msgs, (uint)index, 0);
            } while (s < 0 && Libc.Errno() == Libc.EINTR);
            if (s < 0)
            {
                var errno = Libc.Errno();
                if (errno == Libc.EAGAIN) return totalSent;
                Libc.ThrowErrno("sendmmsg(FD)", "Failed to send classic CAN frames");
            }
            totalSent += s;

            return totalSent;
        }
    }

    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in ICanFrame frame)
    {
        var ch = (SocketCanBus)bus;
        Libc.can_frame* cfBuf = stackalloc Libc.can_frame[1];
        Libc.canfd_frame* fdBuf = stackalloc Libc.canfd_frame[1];
        Libc.iovec* iov = stackalloc Libc.iovec[1];
        Libc.mmsghdr* msgs = stackalloc Libc.mmsghdr[1];
        var sizeClassic = Marshal.SizeOf<Libc.can_frame>();
        var sizeFd = Marshal.SizeOf<Libc.canfd_frame>();
        if (frame is CanFdFrame fdf)
        {
            fdBuf[0] = fdf.ToCanFrame();
            iov[0].iov_base = &fdBuf[0];
            iov[0].iov_len = (UIntPtr)sizeFd;
        }
        else if (frame is CanClassicFrame ccf)
        {
            cfBuf[0] = ccf.ToCanFrame();
            iov[0].iov_base = &cfBuf[0];
            iov[0].iov_len = (UIntPtr)sizeClassic;
        }
        else
        {
            throw new InvalidOperationException(
                "SocketCanFdTransceiver requires CanClassicFrame/CanFdFrame");
        }

        msgs[0].msg_hdr = new Libc.msghdr
        {
            msg_name = null,
            msg_namelen = 0,
            msg_iov = &iov[0],
            msg_iovlen = (UIntPtr)1,
            msg_control = null,
            msg_controllen = UIntPtr.Zero,
            msg_flags = 0
        };
        msgs[0].msg_len = 0;
        int sent;
        do
        {
            sent = Libc.sendmmsg(ch.Handle, msgs, 1, 0);
        } while (sent < 0 && Libc.Errno() == Libc.EINTR);
        if (sent < 0)
        {
            var errno = Libc.Errno();
            if (errno == Libc.EAGAIN) return 0;
            Libc.ThrowErrno("sendmmsg(FD)", "Failed to send classic CAN frames");
        }

        return sent;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int _ = -1)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var batchResult = new List<CanReceiveData>(Libc.BATCH_COUNT);
        var ch = (SocketCanBus)bus;
        var preferTs = ch.Options.PreferKernelTimestamp;
        bool inf = count == 0;
        while (inf || count > 0)
        {
            var end = ReceiveBatch(ch, Math.Min(Libc.BATCH_COUNT, count), preferTs, batchResult);
            count -= batchResult.Count;
            foreach (var r in batchResult)
                yield return r;
            batchResult.Clear();
            if (end)
                yield break;
        }
    }

    private static unsafe bool ReceiveBatch(SocketCanBus bus, int oneBatch, bool preferTs, List<CanReceiveData> result)
    {
        var fd = bus.Handle;
        var sizeClassic = Marshal.SizeOf<Libc.can_frame>();
        var sizeFd = Marshal.SizeOf<Libc.canfd_frame>();
        Libc.canfd_frame* fdBuf = stackalloc Libc.canfd_frame[Libc.BATCH_COUNT];
        Libc.iovec* iov = stackalloc Libc.iovec[Libc.BATCH_COUNT];
        Libc.mmsghdr* msgs = stackalloc Libc.mmsghdr[Libc.BATCH_COUNT];
        int recvd;
        if (preferTs)
        {
            byte* cbase = stackalloc byte[Libc.BATCH_COUNT * 256];
            for (int i = 0; i < Libc.BATCH_COUNT; i++)
            {
                iov[i].iov_base = &fdBuf[i];
                iov[i].iov_len = (UIntPtr)sizeFd;
                msgs[i].msg_hdr = new Libc.msghdr
                {
                    msg_name = null,
                    msg_namelen = 0,
                    msg_iov = &iov[i],
                    msg_iovlen = (UIntPtr)1,
                    msg_control = (cbase + (i * 256)),
                    msg_controllen = (UIntPtr)256,
                    msg_flags = 0
                };
                msgs[i].msg_len = 0;
            }
            do
            {
                recvd = Libc.recvmmsg(fd, msgs, (uint)oneBatch, 0, null);
            }
            while (recvd < 0 && Libc.Errno() == Libc.EINTR);
            if (recvd < 0)
            {
                var errno = Libc.Errno();
                if (errno == Libc.EAGAIN) return true;
                Libc.ThrowErrno("recvmmsg(FD)", "Failed to read classic CAN frames");
            }
            for (int i = 0; i < recvd; i++)
            {
                var msg = msgs[i].msg_hdr;
                var tsSpan = ExtractTimeSpan(ref msg);
                BuildFromFdOrClassic(&fdBuf[i], bus, (int)msgs[i].msg_len, msg.msg_flags, tsSpan, result, sizeClassic, sizeFd);
            }
        }
        else
        {
            for (int i = 0; i < Libc.BATCH_COUNT; i++)
            {
                iov[i].iov_base = &fdBuf[i];
                iov[i].iov_len = (UIntPtr)sizeFd;
                msgs[i].msg_hdr = new Libc.msghdr
                {
                    msg_name = null,
                    msg_namelen = 0,
                    msg_iov = &iov[i],
                    msg_iovlen = (UIntPtr)1,
                    msg_control = null,
                    msg_controllen = UIntPtr.Zero,
                    msg_flags = 0
                };
                msgs[i].msg_len = 0;
            }
            do
            {
                recvd = Libc.recvmmsg(fd, msgs, (uint)oneBatch, 0, null);
            }
            while (recvd < 0 && Libc.Errno() == Libc.EINTR);
            if (recvd < 0)
            {
                var errno = Libc.Errno();
                if (errno == Libc.EAGAIN) return true;
                Libc.ThrowErrno("recvmmsg(FD)", "Failed to read classic CAN frames");
            }
            for (int i = 0; i < recvd; i++)
            {
                var msg = msgs[i].msg_hdr;
                var tsSpan = ExtractTimeSpan(ref msg);
                BuildFromFdOrClassic(&fdBuf[i], bus, (int)msgs[i].msg_len, msg.msg_flags, tsSpan, result, sizeClassic, sizeFd);
            }
        }
        return false;
    }

    private static unsafe void BuildFromFdOrClassic(Libc.canfd_frame* buf, ICanBus bus, int n, int flag,
        TimeSpan tsSpan, List<CanReceiveData> acc, int sizeClassic, int sizeFd)
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
            var data = bus.Options.BufferAllocator.Rent(dataLen);
            fixed (byte* pData = data.Memory.Span)
            {
                Unsafe.CopyBlockUnaligned(pData, buf->data, (uint)Math.Min(dataLen, 64));
            }
            bool brs = (buf->flags & Libc.CANFD_BRS) != 0;
            bool esi = (buf->flags & Libc.CANFD_ESI) != 0;
            bool err = (buf->flags & Libc.CAN_ERR_FLAG) != 0;
            bool ext = (buf->can_id & Libc.CAN_EFF_FLAG) != 0;
            var rawIdFd = (ext)
                ? (buf->can_id & Libc.CAN_EFF_MASK)
                : (buf->can_id & Libc.CAN_SFF_MASK);
            acc.Add(new CanReceiveData(new CanFdFrame((int)rawIdFd, data, brs, esi, ext,
                    bus.Options.BufferAllocator.FrameNeedDispose)
            { IsErrorFrame = err })
            {
                ReceiveTimestamp = tsSpan,
                IsEcho = (flag & Libc.MSG_CONFIRM) != 0
            });
        }
        else if (n == sizeClassic)
        {
            var cf = (Libc.can_frame*)buf;
            int dataLen = cf->can_dlc;
            var data2 = bus.Options.BufferAllocator.Rent(dataLen);
            fixed (byte* pData = data2.Memory.Span)
            {
                Unsafe.CopyBlockUnaligned(pData, cf->data, (uint)Math.Min(dataLen, 8));
            }
            bool err = (cf->can_id & Libc.CAN_ERR_FLAG) != 0;
            bool ext = (cf->can_id & Libc.CAN_EFF_FLAG) != 0;
            bool rtr = (cf->can_id & Libc.CAN_RTR_FLAG) != 0;
            var rawIdC = (ext)
                ? (cf->can_id & Libc.CAN_EFF_MASK)
                : (cf->can_id & Libc.CAN_SFF_MASK);
            acc.Add(new CanReceiveData(new CanClassicFrame((int)rawIdC, data2, ext, rtr,
                bus.Options.BufferAllocator.FrameNeedDispose)
            { IsErrorFrame = err })
            {
                ReceiveTimestamp = tsSpan,
                IsEcho = (flag & Libc.MSG_CONFIRM) != 0
            });
        }
    }

    private static unsafe TimeSpan ExtractTimeSpan(ref Libc.msghdr msg)
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
