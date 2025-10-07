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
        using var e = frames.GetEnumerator();
        if (!e.MoveNext()) return 0;
        var first = e.Current;
        if (e.MoveNext())
            throw new InvalidOperationException("SocketCanClassicTransceiver expects a single frame per call");
        if (first.CanFrame is not CanClassicFrame cf)
            throw new InvalidOperationException("SocketCanClassicTransceiver requires CanClassicFrame");
        unsafe
        {
            var frame = cf.ToCanFrame();
            var size = Marshal.SizeOf<Libc.can_frame>();
            var result = Libc.write(((SocketCanBus)channel).FileDescriptor, &frame, (ulong)size);
            if (result < 0)
            {
                if (Libc.Errno() == Libc.ENOBUFS)
                {
                    return 2;
                }
                else
                {
                    Libc.ThrowErrno("write(can_frame)", "Failed to write classic CAN frame");
                }
            }
            return result switch
            {
                > 0 => 1,
                _ => 0,
            };
        }
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, uint count = 1, int _ = -1)
    {
        var size = Marshal.SizeOf<Libc.can_frame>();
        var result = new List<CanReceiveData>();
        var readCount = 0;
        var ch = (SocketCanBus)bus;
        var preferTs = ch.Options.PreferKernelTimestamp;
        unsafe
        {
            var frame = stackalloc Libc.can_frame[1];
            var iov = stackalloc Libc.iovec[1];
            var cbuf = stackalloc byte[256];
            while (readCount < count || count == 0)
            {
                long n;
                TimeSpan tsSpan = TimeSpan.Zero;
                if (preferTs)
                {
                    iov[0].iov_base = frame;
                    iov[0].iov_len = (UIntPtr)size;

                    var msg = new Libc.msghdr
                    {
                        msg_name = null,
                        msg_namelen = 0,
                        msg_iov = iov,
                        msg_iovlen = (UIntPtr)1,
                        msg_control = cbuf,
                        msg_controllen = (UIntPtr)256,
                        msg_flags = 0
                    };
                    n = Libc.recvmsg(ch.FileDescriptor, &msg, 0);
                    if (n > 0)
                    {
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
                                    var dto = DateTimeOffset.FromUnixTimeSeconds(use.tv_sec)
                                        .AddTicks(use.tv_nsec / 100);
                                    var epoch = new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                                    tsSpan = dto - epoch;
                                    break;
                                }
                                else if (hdr->cmsg_type == Libc.SCM_TIMESTAMPNS)
                                {
                                    var t = *(Libc.timespec*)data;
                                    var dto = DateTimeOffset.FromUnixTimeSeconds(t.tv_sec).AddTicks(t.tv_nsec / 100);
                                    var epoch = new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                                    tsSpan = dto - epoch;
                                    break;
                                }
                            }

                            ulong align = (ulong)IntPtr.Size;
                            ulong step = ((hlen + align - 1) / align) * align;
                            c += (long)step;
                        }
                    }
                }
                else
                {
                    n = Libc.read(ch.FileDescriptor, frame, (ulong)size);
                }

                if (n == 0)
                {
                    return result;
                }

                if (n < 0)
                {
                    var errno = Libc.Errno();
                    if (errno == Libc.EAGAIN)
                        return result;
                    if (errno == Libc.EINTR)
                        continue;
                    Libc.ThrowErrno("read(FD)", "Failed to read classic CAN frame");
                }

                int dataLen = frame->can_dlc;
                var data2 = dataLen == 0 ? Array.Empty<byte>() : new byte[dataLen];
                for (int i = 0; i < dataLen && i < 8; i++) data2[i] = frame->data[i];

                if (tsSpan == TimeSpan.Zero)
                {
                    var now = DateTimeOffset.UtcNow;
                    var epoch = new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                    tsSpan = now - epoch;
                }

                result.Add(new CanReceiveData(new CanClassicFrame(frame->can_id, data2))
                { ReceiveTimestamp = tsSpan });
                readCount++;
            }
        }
        return result;
    }
}
