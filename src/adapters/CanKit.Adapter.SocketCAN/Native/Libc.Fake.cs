// FAKE libc backend for unit tests (in-memory SocketCAN)
#if FAKE
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using CanKit.Adapter.SocketCAN.Definitions;
using CanKit.Adapter.SocketCAN.Diagnostics;
using CanKit.Core.Exceptions;
// @formatter:off
#nullable disable
#pragma warning disable IDE0055
#pragma warning disable CS8981

namespace CanKit.Adapter.SocketCAN.Native;

#nullable disable
internal static class Libc
{
    public const int BATCH_COUNT = 64;

    public const int EOPNOTSUPP = 95;
    public const int EPERM = 1;
    public const int EINTR = 4;
    public const int EAGAIN = 11;
    public const int EACCES = 13;
    public const int ENOBUFS = 105;

    public const int AF_CAN = 29;
    public const int SOCK_RAW = 3;
    public const int SOCK_DGRAM = 2;
    public const int CAN_RAW = 1;

    public const int SOL_SOCKET = 1;

    public const int SOL_CAN_RAW = 101;
    public const int CAN_RAW_FILTER = 1;
    public const int CAN_RAW_ERR_FILTER = 2;
    public const int CAN_RAW_LOOPBACK = 3;
    public const int CAN_RAW_RECV_OWN_MSGS = 4;
    public const int CAN_RAW_FD_FRAMES = 5;

    public const int CAN_BCM = 2;

    public const uint TX_SETUP = 1;
    public const uint TX_DELETE = 2;
    public const uint TX_READ = 3;
    public const uint TX_SEND = 4;
    public const uint TX_STATUS = 8;
    public const uint TX_EXPIRED = 9;

    public const uint RX_SETUP = 5;
    public const uint RX_DELETE = 6;

    public const uint SETTIMER = 0x0001;
    public const uint STARTTIMER = 0x0002;
    public const uint TX_COUNTEVT = 0x0004;
    public const uint CAN_FD_FRAME = 0x0800;

    public const uint CAN_EFF_FLAG = 0x80000000U;
    public const uint CAN_RTR_FLAG = 0x40000000U;
    public const uint CAN_ERR_FLAG = 0x20000000U;

    public const uint CAN_SFF_MASK = 0x000007FFU;
    public const uint CAN_EFF_MASK = 0x1FFFFFFFU;
    public const uint CAN_ERR_MASK = 0x1FFFFFFFU;

    // Error class bits (linux/can/error.h)
    public const uint CAN_ERR_TX_TIMEOUT = 0x00000001U;
    public const uint CAN_ERR_LOSTARB    = 0x00000002U;
    public const uint CAN_ERR_CRTL       = 0x00000004U;
    public const uint CAN_ERR_PROT       = 0x00000008U;
    public const uint CAN_ERR_TRX        = 0x00000010U;
    public const uint CAN_ERR_ACK        = 0x00000020U;
    public const uint CAN_ERR_BUSOFF     = 0x00000040U;
    public const uint CAN_ERR_BUSERROR   = 0x00000080U;
    public const uint CAN_ERR_RESTARTED  = 0x00000100U;
    public const uint CAN_ERR_CNT        = 0x00000200U;

    // Controller state details
    public const byte CAN_ERR_CRTL_RX_OVERFLOW = 0x01;
    public const byte CAN_ERR_CRTL_TX_OVERFLOW = 0x02;
    public const byte CAN_ERR_CRTL_RX_WARNING  = 0x04;
    public const byte CAN_ERR_CRTL_TX_WARNING  = 0x08;
    public const byte CAN_ERR_CRTL_RX_PASSIVE  = 0x10;
    public const byte CAN_ERR_CRTL_TX_PASSIVE  = 0x20;
    public const byte CAN_ERR_CRTL_ACTIVE      = 0x40;

    // Protocol violation details
    public const byte CAN_ERR_PROT_BIT      = 0x01;
    public const byte CAN_ERR_PROT_FORM     = 0x02;
    public const byte CAN_ERR_PROT_STUFF    = 0x04;
    public const byte CAN_ERR_PROT_BIT0     = 0x08;
    public const byte CAN_ERR_PROT_BIT1     = 0x10;
    public const byte CAN_ERR_PROT_OVERLOAD = 0x20;
    public const byte CAN_ERR_PROT_ACTIVE   = 0x40;
    public const byte CAN_ERR_PROT_TX       = 0x80;

    // Transceiver status details (data[4])
    public const byte CAN_ERR_TRX_UNSPEC            = 0x00;
    public const byte CAN_ERR_TRX_CANH_NO_WIRE      = 0x04;
    public const byte CAN_ERR_TRX_CANH_SHORT_TO_BAT = 0x05;
    public const byte CAN_ERR_TRX_CANH_SHORT_TO_VCC = 0x06;
    public const byte CAN_ERR_TRX_CANH_SHORT_TO_GND = 0x07;
    public const byte CAN_ERR_TRX_CANL_NO_WIRE      = 0x40;
    public const byte CAN_ERR_TRX_CANL_SHORT_TO_BAT = 0x50;
    public const byte CAN_ERR_TRX_CANL_SHORT_TO_VCC = 0x60;
    public const byte CAN_ERR_TRX_CANL_SHORT_TO_GND = 0x70;
    public const byte CAN_ERR_TRX_CANL_SHORT_TO_CANH= 0x80;

    public const uint SIOCGIFINDEX = 0x8933;
    public const uint FIONREAD = 0x541B;

    public const short POLLIN = 0x0001;
    public const short POLLOUT = 0x0004;
    public const short POLLERR = 0x0008;
    public const short POLLHUP = 0x0010;
    public const short POLLNVAL = 0x0020;

    public const int EPOLLIN = 0x001;
    public const int EPOLLERR = 0x008;
    public const int EPOLL_CTL_ADD = 1;
    public const int EPOLL_CLOEXEC = 0x00080000;

    public const int EFD_SEMAPHORE = 0x1;
    public const int EFD_NONBLOCK = 0x800;
    public const int EFD_CLOEXEC = 0x00080000;

    public const int F_GETFL = 3;
    public const int F_SETFL = 4;
    public const int O_NONBLOCK = 0x800;

    public const byte CANFD_BRS = 0x01;
    public const byte CANFD_ESI = 0x02;

    public const int SO_SNDBUF = 8;
    public const int SO_RCVBUF = 8;
    public const int SO_SNDTIMEO = 21;
    public const int SO_TIMESTAMP = 29;
    public const int SO_TIMESTAMPNS = 35;
    public const int SO_TIMESTAMPING = 37;
    public const int SOF_TIMESTAMPING_SOFTWARE = 1 << 4;
    public const int SOF_TIMESTAMPING_RX_HARDWARE = 1 << 3;
    public const int SOF_TIMESTAMPING_TX_HARDWARE = 1 << 0;
    public const int SOF_TIMESTAMPING_RAW_HARDWARE = 1 << 6;
    public const int SCM_TIMESTAMPNS = SO_TIMESTAMPNS;
    public const int SCM_TIMESTAMPING = SO_TIMESTAMPING;

    public const int SIOCSHWTSTAMP = 0x89b0;

    public const int OK = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct sockaddr_can
    {
        public ushort can_family;
        public int can_ifindex;
        public uint rx_id;
        public uint tx_id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct can_frame
    {
        public uint can_id;
        public byte can_dlc;
        public byte __pad;
        public byte __res0;
        public byte __res1;
        public fixed byte data[8];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct canfd_frame
    {
        public uint can_id;
        public byte len;
        public byte flags;
        public byte __res0;
        public byte __res1;
        public fixed byte data[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct iovec
    {
        public void* iov_base;
        public UIntPtr iov_len;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct msghdr
    {
        public void* msg_name;
        public uint msg_namelen;
        public iovec* msg_iov;
        public UIntPtr msg_iovlen;
        public void* msg_control;
        public UIntPtr msg_controllen;
        public int msg_flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct mmsghdr
    {
        public msghdr msg_hdr;
        public uint msg_len;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct timespec
    {
        public long tv_sec;
        public long tv_nsec;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct cmsghdr
    {
        public UIntPtr cmsg_len;
        public int cmsg_level;
        public int cmsg_type;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct can_filter
    {
        public uint can_id;
        public uint can_mask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct pollfd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct epoll_event
    {
        public uint events;
        public IntPtr data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct timeval
    {
        public long tv_sec;
        public int tv_usec;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct bcm_msg_head
    {
        public uint opcode;
        public uint flags;
        public uint count;
        public timeval ival1;
        public timeval ival2;
        public uint can_id;
        public uint nframes;
    }

    // ---------- Fake ----------
    private abstract class FakeFd
    {
        public int Id { get; }
        protected FakeFd(int id) { Id = id; }
        public virtual void Close() { }
    }

    private sealed class EventFd(int id) : FakeFd(id)
    {
        public ulong Counter;
        public readonly ManualResetEventSlim Ready = new(false);
        public readonly List<EpollFd> Watchers = new();
        public override void Close() => Ready.Set();
    }

    private sealed class EpollFd(int id) : FakeFd(id)
    {
        public readonly List<(FakeFd fd, uint events)> Watch = new();
        public readonly ManualResetEventSlim Ready = new(false);

        public void Add(FakeFd fd, uint events)
        {
            lock (Watch) Watch.Add((fd, events));
        }

        public int Collect(epoll_event[] buf)
        {
            int n = 0;
            lock (Watch)
            {
                foreach (var (fd, ev) in Watch)
                {
                    if (n >= buf.Length) break;
                    if (HasInput(fd))
                    {
                        buf[n++] = new epoll_event { events = ev, data = (IntPtr)fd.Id };
                    }
                }
            }
            if (n > 0) Ready.Reset();
            return n;
        }
    }

    private sealed class RawSocketFd(int id) : FakeFd(id)
    {
        public int IfIndex;
        public bool Bound;
        public bool RecvOwn;
        public bool AllowFd;
        public readonly List<can_filter> Filters = new();
        public readonly ConcurrentQueue<FakeFrame> Rx = new();
        public readonly List<EpollFd> Watchers = new();
    }

    private sealed class BcmSocketFd(int id) : FakeFd(id)
    {
        public int IfIndex;
        public readonly ConcurrentQueue<byte[]> Rx = new();
        public readonly List<EpollFd> Watchers = new();
    }

    private sealed class CanInterface
    {
        public string Name;
        public uint Index;
        public uint NominalBitrate = 500_000;
        public uint DataBitrate = 500_000;
        public int State = 0; // 0=stopped,1=started
        public uint ClockHz = 80_000_000; // 80 MHz
        public uint CtrlMask = 0xFFFFFFFF;
        public uint CtrlFlags = 0;
        public CanInterface(string name, uint index)
        {
            Name = name; Index = index;
        }
    }

    private readonly struct FakeFrame
    {
        public readonly bool IsFd;
        public readonly uint CanId;
        public readonly byte[] Data;
        public FakeFrame(bool isFd, uint canId, byte[] data)
        { IsFd = isFd; CanId = canId; Data = data; }
    }

    private static class World
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<int, FakeFd> Fds = new();
        private static int _nextId = 100;

        public static readonly Dictionary<string, CanInterface> IfacesByName = new Dictionary<string, CanInterface>(StringComparer.Ordinal)
        {
            ["vcan0"] = new CanInterface("vcan0", 1),
            ["vcan1"] = new CanInterface("vcan1", 2),
            ["vcan2"] = new CanInterface("vcan2", 3),
        };
        public static readonly Dictionary<uint, CanInterface> IfacesByIndex = new Dictionary<uint, CanInterface>
        {
            [1] = IfacesByName["vcan0"],
            [2] = IfacesByName["vcan1"],
            [3] = IfacesByName["vcan2"],
        };

        [ThreadStatic]
        private static int _errno;

        public static int Errno
        {
            get => _errno;
            set => _errno = value;
        }

        public static int NewId()
        {
            lock (Gate) return _nextId++;
        }

        public static void Register(FakeFd fd)
        {
            lock (Gate) Fds[fd.Id] = fd;
        }

        public static T Get<T>(FileDescriptorHandle h) where T : FakeFd
        {
            int id = h.DangerousGetHandle().ToInt32();
            lock (Gate)
            {
                if (Fds.TryGetValue(id, out var fd) && fd is T t) return t;
            }
            ThrowErrno("getfd", $"Invalid FD {id}", EACCES);
            throw new InvalidOperationException();
        }

        public static FakeFd TryGet(FileDescriptorHandle h)
        {
            int id = h.DangerousGetHandle().ToInt32();
            lock (Gate) return Fds.TryGetValue(id, out var fd) ? fd : null;
        }

        public static void Close(int id)
        {
            lock (Gate)
            {
                if (Fds.TryGetValue(id, out var fd))
                {
                    Fds.Remove(id);
                    fd.Close();
                }
            }
        }

        public static IEnumerable<RawSocketFd> RawSockets()
        {
            lock (Gate)
            {
                foreach (var fd in Fds.Values)
                    if (fd is RawSocketFd r) yield return r;
            }
        }
    }

    private static bool HasInput(FakeFd fd)
    {
        return fd switch
        {
            RawSocketFd r => !r.Rx.IsEmpty,
            EventFd e => e.Counter > 0,
            BcmSocketFd b => !b.Rx.IsEmpty,
            _ => false
        };
    }

    private static void SignalWatchers(FakeFd fd)
    {
        switch (fd)
        {
            case RawSocketFd r:
                foreach (var ep in r.Watchers.ToArray()) ep.Ready.Set();
                break;
            case BcmSocketFd b:
                foreach (var ep in b.Watchers.ToArray()) ep.Ready.Set();
                break;
            case EventFd e:
                foreach (var ep in e.Watchers.ToArray()) ep.Ready.Set();
                e.Ready.Set();
                break;
        }
    }

    // --------- Fake API ----------
    public static FileDescriptorHandle socket(int domain, int type, int protocol)
    {
        if (domain != AF_CAN)
        {
            World.Errno = EOPNOTSUPP;
            return new FileDescriptorHandle(new IntPtr(-1), false);
        }
        int id = World.NewId();
        FakeFd fd = (type, protocol) switch
        {
            (SOCK_RAW, CAN_RAW) => new RawSocketFd(id),
            (SOCK_DGRAM, CAN_BCM) => new BcmSocketFd(id),
            _ => null!
        };
        if (fd == null)
        {
            World.Errno = EOPNOTSUPP;
            return new FileDescriptorHandle(new IntPtr(-1), false);
        }
        World.Register(fd);
        return new FileDescriptorHandle(new IntPtr(id), true);
    }

    public static int bind(FileDescriptorHandle sockfd, ref sockaddr_can addr, int addrlen)
    {
        var s = World.Get<RawSocketFd>(sockfd);
        if (addr.can_ifindex <= 0 || !World.IfacesByIndex.ContainsKey((uint)addr.can_ifindex))
        {
            World.Errno = EACCES; return -1;
        }
        s.IfIndex = addr.can_ifindex;
        s.Bound = true;
        return OK;
    }

    public static int connect(FileDescriptorHandle sockfd, ref sockaddr_can addr, int addrlen)
    {
        var b = World.Get<BcmSocketFd>(sockfd);
        if (addr.can_ifindex <= 0 || !World.IfacesByIndex.ContainsKey((uint)addr.can_ifindex))
        { World.Errno = EACCES; return -1; }
        b.IfIndex = addr.can_ifindex;
        return OK;
    }

    public static int fcntl(FileDescriptorHandle fd, int cmd, int arg)
    {
        // We don’t emulate flags precisely. Just succeed.
        return cmd == F_GETFL ? 0 : 0;
    }

    public static int setsockopt(FileDescriptorHandle sockfd, int level, int optname, ref int optval, uint optlen)
    {
        var fd = World.TryGet(sockfd);
        if (fd is RawSocketFd r)
        {
            if (level == SOL_CAN_RAW)
            {
                if (optname == CAN_RAW_RECV_OWN_MSGS) { r.RecvOwn = optval != 0; return OK; }
                if (optname == CAN_RAW_FD_FRAMES) { r.AllowFd = optval != 0; return OK; }
                if (optname == CAN_RAW_ERR_FILTER) { return OK; }
            }
            if (level == SOL_SOCKET)
            {
                // Accept silently
                return OK;
            }
        }
        return OK;
    }

    public static unsafe int setsockopt(FileDescriptorHandle sockfd, int level, int optname, void* optval, uint optlen)
    {
        var fd = World.TryGet(sockfd);
        if (fd is RawSocketFd r && level == SOL_CAN_RAW && optname == CAN_RAW_FILTER)
        {
            r.Filters.Clear();
            if (optval == null || optlen == 0) return OK;
            int elem = Marshal.SizeOf<can_filter>();
            int count = (int)(optlen / (uint)elem);
            var tmp = new Span<can_filter>(new can_filter[count]);
            fixed (can_filter* p = tmp)
            {
                Buffer.MemoryCopy(optval, p, elem * (long)count, elem * (long)count);
            }
            r.Filters.AddRange(tmp.ToArray());
            return OK;
        }
        return OK;
    }

    public static int ioctl(FileDescriptorHandle fd, uint request, ref int argp) => 0;
    public static int ioctl(FileDescriptorHandle fd, uint request, IntPtr argp) => 0;

    public static unsafe long read(FileDescriptorHandle fd, void* buf, ulong count)
    {
        var f = World.TryGet(fd);
        switch (f)
        {
            case EventFd e:
                if (e.Counter == 0) { World.Errno = EAGAIN; return -1; }
                if (count < (ulong)sizeof(ulong)) { World.Errno = ENOBUFS; return -1; }
                Unsafe.Write((ulong*)buf, e.Counter);
                e.Counter = 0;
                e.Ready.Reset();
                return sizeof(ulong);
            case BcmSocketFd b:
                if (!b.Rx.TryDequeue(out var payload)) { World.Errno = EAGAIN; return -1; }
                ulong n = (ulong)Math.Min((int)count, payload.Length);
                fixed (byte* p = payload)
                {
                    Buffer.MemoryCopy(p, buf, (long)count, (long)n);
                }
                return (long)n;
            default:
                World.Errno = EOPNOTSUPP; return -1;
        }
    }

    public static unsafe long write(FileDescriptorHandle fd, void* buf, ulong count)
    {
        var f = World.TryGet(fd);
        switch (f)
        {
            case EventFd e:
                if (count < (ulong)sizeof(ulong)) { World.Errno = ENOBUFS; return -1; }
                var v = Unsafe.Read<ulong>(buf);
                e.Counter += v;
                e.Ready.Set();
                return sizeof(ulong);
            case BcmSocketFd b:
                // Interpret BCM commands
                if (count < (ulong)Marshal.SizeOf<bcm_msg_head>()) { World.Errno = ENOBUFS; return -1; }
                var head = Unsafe.Read<bcm_msg_head>(buf);
                if (head.opcode == TX_SETUP)
                {
                    // Interpret frame immediately following head if present
                    int headSize = Marshal.SizeOf<bcm_msg_head>();
                    var ptr = (byte*)buf + headSize;
                    uint canId = head.can_id;
                    // schedule periodic job
                    var ifc = World.IfacesByIndex[(uint)b.IfIndex];
                    bool isFd = (head.flags & CAN_FD_FRAME) != 0;
                    int frameSize = isFd ? Marshal.SizeOf<canfd_frame>() : Marshal.SizeOf<can_frame>();
                    var payload = new byte[frameSize];
                    if (head.nframes > 0 && (long)count >= headSize + frameSize)
                        Marshal.Copy((IntPtr)ptr, payload, 0, frameSize);

                    // configure periodic loop
                    var period = ToTimeSpan(head.ival1);
                    var inf = head.count == 0; // our convention from higher level
                    if (inf && period == TimeSpan.Zero) period = ToTimeSpan(head.ival2);
                    int remaining = inf ? -1 : (int)head.count;

                    // launch a background sender for this job
                    _ = RunBcmJobAsync(b, canId, payload, isFd, period, remaining);
                    return (long)(headSize + (head.nframes > 0 ? frameSize : 0));
                }
                else if (head.opcode == TX_DELETE)
                {
                    // No persistent state stored; treat as best-effort stop by enqueuing a TX_EXPIRED signal
                    var done = new bcm_msg_head { opcode = TX_EXPIRED };
                    var bytes = StructureToBytes(done);
                    b.Rx.Enqueue(bytes);
                    SignalWatchers(b);
                    return (long)Marshal.SizeOf<bcm_msg_head>();
                }
                else if (head.opcode == TX_READ)
                {
                    // Return minimal TX_STATUS with 0 remaining (non-persistent for simplicity)
                    var status = new bcm_msg_head { opcode = TX_STATUS, count = 0 };
                    b.Rx.Enqueue(StructureToBytes(status));
                    return (long)Marshal.SizeOf<bcm_msg_head>();
                }
                else if (head.opcode == TX_SEND)
                {
                    // one-shot immediate send; treat like raw send of attached frame
                    int headSize = Marshal.SizeOf<bcm_msg_head>();
                    var ptr = (byte*)buf + headSize;
                    bool isFd = (head.flags & CAN_FD_FRAME) != 0;
                    int frameSize = isFd ? Marshal.SizeOf<canfd_frame>() : Marshal.SizeOf<can_frame>();
                    var payload = new byte[frameSize];
                    Marshal.Copy((IntPtr)ptr, payload, 0, frameSize);
                    EmitFrame(b.IfIndex, payload, isFd, sourceFd: null);
                    return (headSize + frameSize);
                }
                return Marshal.SizeOf<bcm_msg_head>();
            default:
                World.Errno = EOPNOTSUPP; return -1;
        }
    }

    private static async Task RunBcmJobAsync(BcmSocketFd bc, uint canIdOrPayloadMarker, byte[] payload, bool isFd, TimeSpan period, int remaining)
    {
        // Encode id into payload if not provided
        _ = canIdOrPayloadMarker;
        if (period <= TimeSpan.Zero) period = TimeSpan.FromMilliseconds(1);
        int left = remaining;
        while (left != 0)
        {
            try
            {
                EmitFrame(bc.IfIndex, payload, isFd, sourceFd: null);
                if (left > 0) left--;
            }
            catch { /* ignore */ }
            await System.Threading.Tasks.Task.Delay(period).ConfigureAwait(false);
        }
        // signal complete
        var done = new bcm_msg_head { opcode = TX_EXPIRED };
        bc.Rx.Enqueue(StructureToBytes(done));
        SignalWatchers(bc);
    }

    private static void EmitFrame(int srcIfIndex, byte[] payload, bool isFd, RawSocketFd sourceFd = null)
    {
        uint canId;
        byte[] data;
        if (isFd)
        {
            unsafe
            {
                fixed (byte* p = payload)
                {
                    canId = Unsafe.ReadUnaligned<uint>(p + 0);
                    int len = *(p + 4);
                    data = new byte[Math.Min(len, 64)];
                    for (int i = 0; i < data.Length; i++) data[i] = p[8 + i];
                }
            }
        }
        else
        {
            unsafe
            {
                fixed (byte* p = payload)
                {
                    canId = Unsafe.ReadUnaligned<uint>(p + 0);
                    int dlc = *(p + 4);
                    data = new byte[Math.Min(dlc, 8)];
                    for (int i = 0; i < data.Length; i++) data[i] = p[8 + i];
                }
            }
        }

        foreach (var target in World.RawSockets())
        {
            // Skip unbound
            if (!target.Bound) continue;
            // echo control
            if (sourceFd != null && ReferenceEquals(target, sourceFd) && !target.RecvOwn) continue;

            // bitrate compatibility: same nominal bitrate only
            var src = World.IfacesByIndex[(uint)srcIfIndex];
            var dst = World.IfacesByIndex[(uint)target.IfIndex];
            if (src.NominalBitrate != dst.NominalBitrate) continue;

            // filters
            if (!MatchFilters(target.Filters, canId)) continue;

            // build frame struct back to target’s expectation (keep as raw bytes to simplify)
            target.Rx.Enqueue(new FakeFrame(isFd, canId, data));
            SignalWatchers(target);
        }
    }

    private static bool MatchFilters(List<can_filter> filters, uint canId)
    {
        if (filters.Count == 0) return true;
        foreach (var f in filters)
        {
            if (((canId & f.can_mask) == (f.can_id & f.can_mask))) return true;
        }
        return false;
    }

    public static unsafe int recvmmsg(FileDescriptorHandle sockfd, mmsghdr* msgvec, uint vlen, int flags, timespec* timeout)
    {
        var r = World.Get<RawSocketFd>(sockfd);
        int received = 0;
        int max = (int)vlen;
        while (received < max)
        {
            if (!r.Rx.TryDequeue(out var fx)) break;
            ref var msg = ref msgvec[received];
            var iov = *(msg.msg_hdr.msg_iov);
            int want = (int)iov.iov_len;

            if (fx.IsFd)
            {
                var outSize = Marshal.SizeOf<canfd_frame>();
                if (want >= outSize)
                {
                    byte* dest = (byte*)iov.iov_base;
                    Unsafe.WriteUnaligned(dest + 0, fx.CanId);
                    dest[4] = (byte)Math.Min(64, fx.Data.Length);
                    dest[5] = 0; dest[6] = 0; dest[7] = 0;
                    int copy = Math.Min(fx.Data.Length, 64);
                    for (int k = 0; k < copy; k++) dest[8 + k] = fx.Data[k];
                    msg.msg_len = (uint)outSize;
                }
                else
                {
                    // not enough space, drop
                    msg.msg_len = 0;
                }
            }
            else
            {
                var outSize = Marshal.SizeOf<can_frame>();
                if (want >= outSize)
                {
                    byte* dest = (byte*)iov.iov_base;
                    Unsafe.WriteUnaligned<uint>(dest + 0, fx.CanId);
                    dest[4] = (byte)Math.Min(8, fx.Data.Length);
                    dest[5] = 0; dest[6] = 0; dest[7] = 0;
                    int copy = Math.Min(fx.Data.Length, 8);
                    for (int k = 0; k < copy; k++) dest[8 + k] = fx.Data[k];
                    msg.msg_len = (uint)outSize;
                }
                else
                {
                    msg.msg_len = 0;
                }
            }
            received++;
        }

        if (received == 0)
        {
            World.Errno = EAGAIN; return -1;
        }
        return received;
    }

    public static unsafe int sendmmsg(FileDescriptorHandle sockfd, mmsghdr* msgvec, uint vlen, int flags)
    {
        var s = World.Get<RawSocketFd>(sockfd);
        int sent = 0;
        int max = (int)vlen;
        for (int i = 0; i < max; i++)
        {
            var msg = msgvec[i].msg_hdr;
            var iov = *(msg.msg_iov);
            int len = (int)iov.iov_len;
            bool isFd = len == Marshal.SizeOf<canfd_frame>();
            var payload = new byte[len];
            Marshal.Copy((IntPtr)iov.iov_base, payload, 0, len);
            EmitFrame(s.IfIndex, payload, isFd, s);
            sent++;
        }
        return sent;
    }

    public static int close(int fd)
    {
        World.Close(fd);
        return 0;
    }

    public static int poll(ref pollfd fds, uint nfds, int timeout)
    {
        _ = nfds;
        _ = timeout;
        // Simplify: always writable
        fds.revents = POLLOUT;
        return 1;
    }

    public static FileDescriptorHandle epoll_create1(int _)
    {
        int id = World.NewId();
        var ep = new EpollFd(id);
        World.Register(ep);
        return new FileDescriptorHandle(new IntPtr(id), true);
    }

    public static int epoll_ctl(FileDescriptorHandle epfd, int op, FileDescriptorHandle fd, ref epoll_event ev)
    {
        var ep = World.Get<EpollFd>(epfd);
        var target = World.TryGet(fd);
        if (target == null) { World.Errno = EACCES; return -1; }
        if (op == EPOLL_CTL_ADD)
        {
            ep.Add(target, ev.events);
            switch (target)
            {
                case RawSocketFd r: r.Watchers.Add(ep); break;
                case BcmSocketFd b: b.Watchers.Add(ep); break;
                case EventFd e: e.Watchers.Add(ep); break;
            }
        }
        return OK;
    }

    public static int epoll_wait(FileDescriptorHandle epfd, [In, Out] epoll_event[] events, int maxevents, int timeout)
    {
        var ep = World.Get<EpollFd>(epfd);
        int n = ep.Collect(events);
        if (n > 0) return n;
        if (timeout == 0) return 0;
        // Wait until something signals
        if (timeout < 0)
        {
            ep.Ready.Wait();
        }
        else
        {
            ep.Ready.Wait(timeout);
        }
        return ep.Collect(events);
    }

    public static FileDescriptorHandle eventfd(uint initval, int _)
    {
        int id = World.NewId();
        var ev = new EventFd(id) { Counter = initval };
        World.Register(ev);
        return new FileDescriptorHandle(new IntPtr(id), true);
    }

    public static uint if_nametoindex(string ifname)
    {
        return World.IfacesByName.TryGetValue(ifname, out var i) ? i.Index : 0;
    }

    public static IntPtr if_indextoname(uint index, byte[] ifname)
    {
        if (World.IfacesByIndex.TryGetValue(index, out var i))
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(i.Name + "\0");
            Array.Copy(bytes, ifname, Math.Min(bytes.Length, ifname.Length));
            return new IntPtr(1);
        }
        return IntPtr.Zero;
    }

    public static string StrError(int errno)
    {
        return errno switch
        {
            EAGAIN => "Resource temporarily unavailable",
            EINTR => "Interrupted system call",
            EACCES => "Permission denied",
            EOPNOTSUPP => "Operation not supported",
            _ => $"Error {errno}"
        };
    }

    public static void ThrowErrno(string operation, string message)
    {
        var err = (uint)Errno();
        throw new SocketCanNativeException(operation, message, err);
    }

    public static int Errno() => World.Errno;

    public static void ThrowErrno(string operation, string message, int errno)
    {
        World.Errno = errno;
        throw new SocketCanNativeException(operation, message, (uint)errno);
    }

    private static TimeSpan ToTimeSpan(timeval tv)
        => TimeSpan.FromSeconds(tv.tv_sec) + TimeSpan.FromMilliseconds(tv.tv_usec / 1000.0);

    private static byte[] StructureToBytes<T>(T value) where T : struct
    {
        int size = Unsafe.SizeOf<T>();
        var ret = new byte[size];
#if NET5_0_OR_GREATER
        MemoryMarshal.Write(ret.AsSpan(), in value);
#else
        MemoryMarshal.Write(ret.AsSpan(), ref value);
#endif
        return ret;
    }
}
#endif
