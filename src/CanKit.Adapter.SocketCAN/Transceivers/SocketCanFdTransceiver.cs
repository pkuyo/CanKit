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
            if (first.CanFrame is CanFdFrame ff)
            {
                var frame = ff.ToCanFrame();
                var size = Marshal.SizeOf<Libc.canfd_frame>();
                var result = Libc.write(((SocketCanBus)channel).FileDescriptor, &frame, (ulong)size);
                return result switch
                {
                    > 0 => 1,
                    0 => 0,
                    _ => throw new CanNativeCallException("write(canfd_frame)", "Failed to write CAN FD frame",
                        (uint)Marshal.GetLastWin32Error())
                };
            }
            else if (first.CanFrame is CanClassicFrame cf)
            {
                var frame = cf.ToCanFrame();
                var size = Marshal.SizeOf<Libc.can_frame>();
                var result = Libc.write(((SocketCanBus)channel).FileDescriptor, &frame, (ulong)size);
                return result switch
                {
                    > 0 => 1,
                    0 => 0,
                    _ => throw new CanNativeCallException("write(can_frame)", "Failed to write classic CAN frame",
                        (uint)Marshal.GetLastWin32Error())
                };
            }
            else
            {
                throw new InvalidOperationException("SocketCanFdTransceiver requires CanClassicFrame/CanFdFrame");
            }
        }
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> channel, uint count = 1, int _ = -1)
    {
        var size = Marshal.SizeOf<Libc.canfd_frame>();
        var result = new List<CanReceiveData>();
        var readCount = 0;
        var ch = (SocketCanBus)channel;
        var preferTs = ch.Options.PreferKernelTimestamp;
        unsafe
        {
            var frame = stackalloc Libc.canfd_frame[1];
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
                        msg_iovlen = (UIntPtr)1, //Cast for net standard 2.0
                        msg_control = cbuf,
                        msg_controllen = (UIntPtr)256, //Cast for net standard 2.0
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
                                    var dto = DateTimeOffset.FromUnixTimeSeconds(use.tv_sec)
                                        .AddTicks(use.tv_nsec / 100);
                                    var epoch = new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                                    tsSpan = dto - epoch;
                                    break;
                                }
                                else if (hdr->cmsg_type == Libc.SCM_TIMESTAMPNS)
                                {
                                    var t = *(Libc.timespec*)data2;
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
                    if(errno == Libc.EINTR)
                        continue;
                    Libc.ThrowErrno("read(FD)", "Failed to read classic/FD CAN frame");
                }

                if (tsSpan == TimeSpan.Zero)
                {
                    var now = DateTimeOffset.UtcNow;
                    var epoch = new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                    tsSpan = now - epoch;
                }

                if (size == n)
                {
                    //receive canfd_frame
                    int dataLen = frame->len;
                    var data = dataLen == 0 ? [] : new byte[dataLen];
                    for (int i = 0; i < dataLen && i < 64; i++) data[i] = frame->data[i];

                    bool brs = (frame->flags & Libc.CANFD_BRS) != 0;
                    bool esi = (frame->flags & Libc.CANFD_ESI) != 0;
                    bool err = (frame->flags & Libc.CAN_ERR_FLAG) != 0;

                    result.Add(new CanReceiveData(new CanFdFrame(frame->can_id, data, brs, esi)
                    { IsErrorFrame = err })
                    { ReceiveTimestamp = tsSpan });
                }
                else
                {
                    //receive can_frame
                    var cf = (Libc.can_frame*)frame;
                    int dataLen = cf->can_dlc;
                    var data2 = dataLen == 0 ? Array.Empty<byte>() : new byte[dataLen];
                    for (int i = 0; i < dataLen && i < 8; i++) data2[i] = frame->data[i];

                    if (tsSpan == TimeSpan.Zero)
                    {
                        var now = DateTimeOffset.UtcNow;
                        var epoch = new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                        tsSpan = now - epoch;
                    }

                    bool err = (frame->flags & Libc.CAN_ERR_FLAG) != 0;

                    result.Add(new CanReceiveData(new CanClassicFrame(frame->can_id, data2)
                    { IsErrorFrame = err })
                    { ReceiveTimestamp = tsSpan });
                }
            }
        }
        return result;
    }
}
