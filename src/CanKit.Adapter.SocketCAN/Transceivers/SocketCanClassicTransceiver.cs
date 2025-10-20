using System;
using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CanKit.Adapter.SocketCAN.Native;
using CanKit.Adapter.SocketCAN.Definitions;
using CanKit.Adapter.SocketCAN.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.SocketCAN.Transceivers;

public sealed class SocketCanClassicTransceiver : ITransceiver
{
    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<ICanFrame> frames)
    {

        var ch = (SocketCanBus)bus;
        var totalSent = 0;
        var index = 0;
        unsafe
        {
            var frameSize = Marshal.SizeOf<Libc.can_frame>();
            Libc.can_frame* fr = stackalloc Libc.can_frame[Libc.BATCH_COUNT];
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
                var cf = (CanClassicFrame)f;
                fr[index] = cf.ToCanFrame();
                iov[index].iov_base = &fr[index];
                iov[index].iov_len = (UIntPtr)frameSize;
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
            var frameSize = Marshal.SizeOf<Libc.can_frame>();
            Libc.can_frame* fr = stackalloc Libc.can_frame[Libc.BATCH_COUNT];
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
                var cf = (CanClassicFrame)f;
                fr[index] = cf.ToCanFrame();
                iov[index].iov_base = &fr[index];
                iov[index].iov_len = (UIntPtr)frameSize;
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

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in ICanFrame frame)
    {
        var ch = (SocketCanBus)bus;
        unsafe
        {
            var frameSize = Marshal.SizeOf<Libc.can_frame>();
            Libc.can_frame* fr = stackalloc Libc.can_frame[Libc.BATCH_COUNT];
            Libc.iovec* iov = stackalloc Libc.iovec[Libc.BATCH_COUNT];
            Libc.mmsghdr* msgs = stackalloc Libc.mmsghdr[Libc.BATCH_COUNT];

            if (frame is not CanClassicFrame cf)
            {
                throw new InvalidOperationException("SocketCAN classic transceiver requires CanClassicFrame.");
            }

            fr[0] = cf.ToCanFrame();
            iov[0].iov_base = &fr[0];
            iov[0].iov_len = (UIntPtr)frameSize;
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
            return 1;
        }
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int _ = -1)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        var batchResult = new List<CanReceiveData>(Libc.BATCH_COUNT);
        var ch = (SocketCanBus)bus;
        var preferTs = ch.Options.PreferKernelTimestamp;
        var inf = count == 0;
        while (inf || count > 0)
        {
            var end = ReceiveBatch(ch.Handle, Math.Min(Libc.BATCH_COUNT, count), preferTs, batchResult);
            count -= batchResult.Count;
            foreach (var r in batchResult)
                yield return r;
            batchResult.Clear();
            if (end)
                yield break;
        }
    }

    private static unsafe bool ReceiveBatch(FileDescriptorHandle fd, int oneBatch, bool preferTs, List<CanReceiveData> result)
    {
        var frameSize = Marshal.SizeOf<Libc.can_frame>();
        Libc.can_frame* fr = stackalloc Libc.can_frame[Libc.BATCH_COUNT];
        Libc.iovec* iov = stackalloc Libc.iovec[Libc.BATCH_COUNT];
        Libc.mmsghdr* msgs = stackalloc Libc.mmsghdr[Libc.BATCH_COUNT];
        int recvd;
        if (preferTs)
        {
            byte* cbase = stackalloc byte[Libc.BATCH_COUNT * 256];
            for (int i = 0; i < Libc.BATCH_COUNT; i++)
            {
                iov[i].iov_base = &fr[i];
                iov[i].iov_len = (UIntPtr)frameSize;
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
                var tsSpan = ExtractTimestamp(ref msg);
                BuildClassic(fr, i, (int)msgs[i].msg_len, tsSpan, result);
            }
        }
        else
        {
            for (int i = 0; i < Libc.BATCH_COUNT; i++)
            {
                iov[i].iov_base = &fr[i];
                iov[i].iov_len = (UIntPtr)frameSize;
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
                var tsSpan = ExtractTimestamp(ref msg);
                BuildClassic(fr, i, (int)msgs[i].msg_len, tsSpan, result);
            }
        }
        return false;
    }
    private static unsafe void BuildClassic(Libc.can_frame* fr, int idx, int n, TimeSpan tsSpan, List<CanReceiveData> result)
    {
        if (n <= 0) return;
        if (tsSpan == TimeSpan.Zero)
        {
            var now = DateTimeOffset.UtcNow;
            tsSpan = now - _epoch;
        }
        int dataLen = fr[idx].can_dlc;
        var data = dataLen == 0 ? Array.Empty<byte>() : new byte[dataLen];
        fixed (byte* pData = data)
        {
            Unsafe.CopyBlockUnaligned(pData, fr[idx].data, (uint)Math.Min(dataLen, 64));
        }
        bool ext = (fr[idx].can_id & Libc.CAN_EFF_FLAG) != 0;
        var rawId = ((fr[idx].can_id & Libc.CAN_EFF_FLAG) != 0)
            ? (fr[idx].can_id & Libc.CAN_EFF_MASK)
            : (fr[idx].can_id & Libc.CAN_SFF_MASK);
        var re = new CanReceiveData(new CanClassicFrame((int)rawId, data, ext))
        { ReceiveTimestamp = tsSpan };
        result.Add(re);
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
