using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.SocketCAN.Native;
using CanKit.Adapter.SocketCAN.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.SocketCAN.Transceivers;

public sealed class SocketCanClassicTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames, int _ = 0)
    {
        var ch = (SocketCanBus)channel;
        var batch = frames.ToArray();
        if (batch.Length == 0) return 0;
        var totalSent = 0;
        unsafe
        {
            var frameSize = Marshal.SizeOf<Libc.can_frame>();
            Libc.can_frame* fr = stackalloc Libc.can_frame[64];
            Libc.iovec* iov = stackalloc Libc.iovec[64];
            Libc.mmsghdr* msgs = stackalloc Libc.mmsghdr[64];
            while (totalSent < batch.Length)
            {
                int n = Math.Min(batch.Length - totalSent, 64);

                for (int i = 0; i < n; i++)
                {
                    var cf = (CanClassicFrame)batch[i + totalSent].CanFrame;
                    fr[i] = cf.ToCanFrame();
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

                int sent;
                do
                {
                    sent = Libc.sendmmsg(ch.FileDescriptor, msgs, (uint)n, 0);
                }
                while (sent < 0 && Libc.Errno() == Libc.EINTR);
                if (sent < 0)
                {
                    var errno = Libc.Errno();
                    if (errno == Libc.EAGAIN) return (uint)totalSent;
                    Libc.ThrowErrno("sendmmsg(FD)", "Failed to send classic CAN frames");
                }

                totalSent += sent;
                if (sent != n)
                    break;
            }
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
            var frameSize = Marshal.SizeOf<Libc.can_frame>();
            Libc.can_frame* fr = stackalloc Libc.can_frame[64];
            Libc.iovec* iov = stackalloc Libc.iovec[64];
            Libc.mmsghdr* msgs = stackalloc Libc.mmsghdr[64];
            byte* cbase = stackalloc byte[64 * 256];
            while (inf || count > 0)
            {
                var oneBatch = (int)Math.Max(1, Math.Min(count == 0 ? 64u : count, 64u));
                for (int i = 0; i < oneBatch; i++)
                {
                    iov[i].iov_base = &fr[i];
                    iov[i].iov_len = (UIntPtr)frameSize;
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
                    Libc.ThrowErrno("recvmmsg(FD)", "Failed to read classic CAN frames");
                }

                count -= (uint)recvd;
                for (int i = 0; i < recvd; i++)
                {
                    var msg = msgs[i].msg_hdr;
                    var tsSpan = ExtractTimestamp(ref msg);
                    BuildClassic(fr, i, (int)msgs[i].msg_len, tsSpan, result);
                }
            }
        }
        return result;
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
            Buffer.MemoryCopy(fr[idx].data, pData, data.Length, Math.Min(dataLen, 64));
        }
        result.Add(new CanReceiveData(new CanClassicFrame(fr[idx].can_id, data))
        { ReceiveTimestamp = tsSpan });
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
