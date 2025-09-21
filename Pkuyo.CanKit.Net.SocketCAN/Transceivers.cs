using System.Runtime.InteropServices;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.SocketCAN.Native;

namespace Pkuyo.CanKit.Net.SocketCAN;

public sealed class SocketCanClassicTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames, int _ = 0)
    {
        if (frames.Single().CanFrame is not CanClassicFrame cf)
            throw new InvalidOperationException("SocketCanTransceiver requires CanClassicFrame for transmission");
        unsafe
        {
            var frame = new Libc.can_frame
            {
                can_id = cf.RawID,
                can_dlc = cf.Dlc,
                __pad = 0,
                __res0 = 0,
                __res1 = 0,
            };
            var src = cf.Data.Span;
            int copy = Math.Min(src.Length, 8);
            for (int i = 0; i < copy; i++) frame.data[i] = src[i];

            var size = Marshal.SizeOf<Libc.can_frame>();
            var result = Libc.write(((SocketCanBus)channel).FileDescriptor, &frame, (ulong)size);
            return result switch
            {
                > 0 => 1,
                0 => 0,
                _ => throw new Exception() // TODO: exception handling
            };
        }
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> channel, uint count = 1, int _ = -1)
    {
        var size = Marshal.SizeOf<Libc.can_frame>();
        var result = new List<CanReceiveData>();
        unsafe
        {
            var ch = (SocketCanBus)channel;
            bool preferTs = ch.Options.PreferKernelTimestamp;

            var frame = stackalloc Libc.can_frame[1];
            long n;
            ulong tsTicks = 0;
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
                                tsTicks = (ulong)dto.UtcDateTime.Ticks;
                                break;
                            }
                            else if (hdr->cmsg_type == Libc.SCM_TIMESTAMPNS)
                            {
                                var t = *(Libc.timespec*)data;
                                var dto = DateTimeOffset.FromUnixTimeSeconds(t.tv_sec).AddTicks(t.tv_nsec / 100);
                                tsTicks = (ulong)dto.UtcDateTime.Ticks;
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
                n = Libc.read(ch.FileDescriptor, frame , (ulong)size);
            }
            if (n <= 0)
            {
                return result;
            }

            int dataLen = frame->can_dlc;
            var data2 = new byte[dataLen];
            for (int i = 0; i < dataLen && i < 8; i++) data2[i] = frame->data[i];

            if (tsTicks == 0) tsTicks = (ulong)DateTime.UtcNow.Ticks;
            result.Add(new CanReceiveData(new CanClassicFrame(frame->can_id, data2))
                { recvTimestamp = tsTicks });
        }
        return result;
    }
}

public sealed class SocketCanFdTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames, int _ = 0)
    {
        if (frames.Single().CanFrame is not CanFdFrame ff)
            throw new InvalidOperationException("SocketCanFdTransceiver requires CanFdFrame for transmission");
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
            int copy = Math.Min(src.Length, 64);
            for (int i = 0; i < copy; i++) frame.data[i] = src[i];

            var size = Marshal.SizeOf<Libc.canfd_frame>();
            var result = Libc.write(((SocketCanBus)channel).FileDescriptor, &frame, (ulong)size);
            return result switch
            {
                > 0 => 1,
                0 => 0,
                _ => throw new Exception() // TODO: exception handling
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
            ulong tsTicks = 0;
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
                                tsTicks = (ulong)dto.UtcDateTime.Ticks;
                                break;
                            }
                            else if (hdr->cmsg_type == Libc.SCM_TIMESTAMPNS)
                            {
                                var t = *(Libc.timespec*)data2;
                                var dto = DateTimeOffset.FromUnixTimeSeconds(t.tv_sec).AddTicks(t.tv_nsec / 100);
                                tsTicks = (ulong)dto.UtcDateTime.Ticks;
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
            var data = new byte[dataLen];
            for (int i = 0; i < dataLen && i < 64; i++) data[i] = frame->data[i];

            bool brs = (frame->flags & Libc.CANFD_BRS) != 0;
            bool esi = (frame->flags & Libc.CANFD_ESI) != 0;
            if (tsTicks == 0) tsTicks = (ulong)DateTime.UtcNow.Ticks;
            result.Add(new CanReceiveData(new CanFdFrame(frame->can_id, data, brs, esi))
                { recvTimestamp = tsTicks });
        }

        return result;
    }
}
