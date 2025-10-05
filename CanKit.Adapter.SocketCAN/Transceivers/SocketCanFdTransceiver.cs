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
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames, int _ = 0)
    {
        unsafe
        {
            using var e = frames.GetEnumerator();
            if (!e.MoveNext()) return 0;
            var first = e.Current;
            if (e.MoveNext())
                throw new InvalidOperationException("SocketCanFdTransceiver expects a single frame per call");
            if (first.CanFrame is not CanFdFrame ff)
                throw new InvalidOperationException("SocketCanFdTransceiver requires CanFdFrame for transmission");

            var frame = ff.ToCanFrame();
            var size = Marshal.SizeOf<Libc.canfd_frame>();
            var result = Libc.write(((SocketCanBus)channel).FileDescriptor, &frame, (ulong)size);
            return result switch
            {
                > 0 => 1,
                0 => 0,
                _ => throw new CanNativeCallException("write(canfd_frame)", "Failed to write CAN FD frame", (uint)Marshal.GetLastWin32Error())
            };
        }
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> channel, uint count = 1, int _ = -1)
    {
        var size = Marshal.SizeOf<Libc.canfd_frame>();
        var result = new List<CanReceiveData>();
        unsafe
        {
            var ch = (SocketCanBus)channel;
            bool preferTs = ch.Options.PreferKernelTimestamp;
            var frame = stackalloc Libc.canfd_frame[1];

            long n;
            TimeSpan tsSpan = TimeSpan.Zero;
            if (preferTs)
            {
                var iov = stackalloc Libc.iovec[1];
                iov[0].iov_base = frame;
                iov[0].iov_len = (UIntPtr)size;
                byte* cbuf = stackalloc byte[256];
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
                        byte* data2 = c + (ulong)Marshal.SizeOf<Libc.cmsghdr>();
                        if (hdr->cmsg_level == Libc.SOL_SOCKET)
                        {
                            if (hdr->cmsg_type == Libc.SCM_TIMESTAMPING)
                            {
                                var t = (Libc.timespec*)data2;
                                var raw = t[2];
                                var sw = t[0];
                                var use = (raw.tv_sec != 0 || raw.tv_nsec != 0) ? raw : sw;
                                var dto = DateTimeOffset.FromUnixTimeSeconds(use.tv_sec).AddTicks(use.tv_nsec / 100);
                                var epoch = new DateTimeOffset(new DateTime(1970,1,1,0,0,0, DateTimeKind.Utc));
                                tsSpan = dto - epoch;
                                break;
                            }
                            else if (hdr->cmsg_type == Libc.SCM_TIMESTAMPNS)
                            {
                                var t = *(Libc.timespec*)data2;
                                var dto = DateTimeOffset.FromUnixTimeSeconds(t.tv_sec).AddTicks(t.tv_nsec / 100);
                                var epoch = new DateTimeOffset(new DateTime(1970,1,1,0,0,0, DateTimeKind.Utc));
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

            if (n <= 0)
            {
                return result;
            }

            int dataLen = frame->len;
            var data = dataLen == 0 ? Array.Empty<byte>() : new byte[dataLen];
            for (int i = 0; i < dataLen && i < 64; i++) data[i] = frame->data[i];

            bool brs = (frame->flags & Libc.CANFD_BRS) != 0;
            bool esi = (frame->flags & Libc.CANFD_ESI) != 0;
            if (tsSpan == TimeSpan.Zero)
            {
                var now = DateTimeOffset.UtcNow;
                var epoch = new DateTimeOffset(new DateTime(1970,1,1,0,0,0, DateTimeKind.Utc));
                tsSpan = now - epoch;
            }
            result.Add(new CanReceiveData(new CanFdFrame(frame->can_id, data, brs, esi))
            { ReceiveTimestamp = tsSpan });
        }
        return result;
    }
}
