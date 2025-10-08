using System.Runtime.InteropServices;
using System.Text;
using CanKit.Adapter.SocketCAN.Diagnostics;
using CanKit.Core.Exceptions;
// @formatter:off
namespace CanKit.Adapter.SocketCAN.Native;
#nullable disable
#pragma warning disable IDE0055
#pragma warning disable CS8981
internal static class Libc
{
    //errno
    public const int EOPNOTSUPP = 95;
    public const int EPERM = 1;
    public const int EINTR = 4;
    public const int EAGAIN = 11;
    public const int EACCES = 13;
    public const int ENOBUFS = 105;

    // Address family / socket types
    public const int AF_CAN = 29;           // Linux specific
    public const int SOCK_RAW = 3;
    public const int SOCK_DGRAM = 2;
    public const int CAN_RAW = 1;

    public const int SOL_SOCKET = 1;

    // SOL_CAN_RAW level and options
    public const int SOL_CAN_RAW = 101;
    public const int CAN_RAW_FILTER = 1;
    public const int CAN_RAW_ERR_FILTER = 2;
    public const int CAN_RAW_LOOPBACK = 3;
    public const int CAN_RAW_RECV_OWN_MSGS = 4;
    public const int CAN_RAW_FD_FRAMES = 5;

    public const int CAN_BCM = 2;

    // bcm opcode/flags (see include/uapi/linux/can/bcm.h)
    public const uint TX_SETUP   = 1;
    public const uint TX_DELETE  = 2;
    public const uint TX_READ    = 3;
    public const uint TX_SEND    = 4;
    public const uint RX_SETUP   = 5;
    public const uint RX_DELETE  = 6;
    public const uint RX_READ    = 7;
    public const uint TX_STATUS  = 8;
    public const uint TX_EXPIRED = 9;
    public const uint RX_STATUS  = 10;
    public const uint RX_TIMEOUT = 11;
    public const uint RX_CHANGED = 12;

    public const uint SETTIMER    = 0x0001;
    public const uint STARTTIMER  = 0x0002;
    public const uint TX_COUNTEVT = 0x0004;
    public const uint TX_ANNOUNCE = 0x0008;
    public const uint TX_CP_CAN_ID = 0x0010;
    public const uint RX_FILTER_ID = 0x0020;
    public const uint RX_CHECK_DLC = 0x0040;
    public const uint RX_NO_AUTOTIMER = 0x0080;
    public const uint RX_ANNOUNCE_RESUME = 0x0100;
    public const uint TX_RESET_MULTI_IDX = 0x0200;
    public const uint RX_RTR_FRAME = 0x0400;
    public const uint CAN_FD_FRAME = 0x0800;


    // CAN ID flags and masks
    public const uint CAN_EFF_FLAG = 0x80000000U; // extended frame format
    public const uint CAN_RTR_FLAG = 0x40000000U; // remote transmission request
    public const uint CAN_ERR_FLAG = 0x20000000U; // error frame flag

    public const uint CAN_SFF_MASK = 0x000007FFU; // standard frame format mask
    public const uint CAN_EFF_MASK = 0x1FFFFFFFU; // extended frame format mask
    public const uint CAN_ERR_MASK = 0x1FFFFFFFU; // error class mask

    // Error class bits (linux/can/error.h)
    public const uint CAN_ERR_TX_TIMEOUT = 0x00000001U; // TX timeout
    public const uint CAN_ERR_LOSTARB    = 0x00000002U; // lost arbitration
    public const uint CAN_ERR_CRTL       = 0x00000004U; // controller problems
    public const uint CAN_ERR_PROT       = 0x00000008U; // protocol violations
    public const uint CAN_ERR_TRX        = 0x00000010U; // transceiver status
    public const uint CAN_ERR_ACK        = 0x00000020U; // received no ACK on transmission
    public const uint CAN_ERR_BUSOFF     = 0x00000040U; // bus off
    public const uint CAN_ERR_BUSERROR   = 0x00000080U; // bus error (may be set with CAN_ERR_PROT)
    public const uint CAN_ERR_RESTARTED  = 0x00000100U; // controller restarted
    public const uint CAN_ERR_CNT        = 0x00000200U; // error counters

    // Controller state details (error.h - CAN_ERR_CRTL)
    public const byte CAN_ERR_CRTL_RX_OVERFLOW = 0x01;
    public const byte CAN_ERR_CRTL_TX_OVERFLOW = 0x02;
    public const byte CAN_ERR_CRTL_RX_WARNING  = 0x04;
    public const byte CAN_ERR_CRTL_TX_WARNING  = 0x08;
    public const byte CAN_ERR_CRTL_RX_PASSIVE  = 0x10;
    public const byte CAN_ERR_CRTL_TX_PASSIVE  = 0x20;
    public const byte CAN_ERR_CRTL_ACTIVE      = 0x40;

    // Protocol violation details (error.h - CAN_ERR_PROT)
    public const byte CAN_ERR_PROT_BIT      = 0x01;
    public const byte CAN_ERR_PROT_FORM     = 0x02;
    public const byte CAN_ERR_PROT_STUFF    = 0x04;
    public const byte CAN_ERR_PROT_BIT0     = 0x08;
    public const byte CAN_ERR_PROT_BIT1     = 0x10;
    public const byte CAN_ERR_PROT_OVERLOAD = 0x20;
    public const byte CAN_ERR_PROT_ACTIVE   = 0x40;
    public const byte CAN_ERR_PROT_TX       = 0x80;

    // Transceiver status details (error.h - CAN_ERR_TRX in data[4])
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

    // ioctls
    public const uint SIOCGIFINDEX = 0x8933; // ifreq.ifr_ifindex
    public const uint FIONREAD = 0x541B;     // bytes available

    // poll
    public const short POLLIN = 0x0001;
    public const short POLLOUT = 0x0004;
    public const short POLLERR = 0x0008;
    public const short POLLHUP = 0x0010;
    public const short POLLNVAL = 0x0020;

    // epoll
    public const int EPOLLIN = 0x001;
    public const int EPOLLERR = 0x008;
    public const int EPOLL_CTL_ADD = 1;
    public const int EPOLL_CLOEXEC = 0x00080000;

    // eventfd (sys/eventfd.h)
    public const int EFD_SEMAPHORE = 0x1;
    public const int EFD_NONBLOCK  = 0x800;
    public const int EFD_CLOEXEC   = 0x00080000;

    // fcntl
    public const int F_GETFL = 3;
    public const int F_SETFL = 4;
    public const int O_NONBLOCK = 0x800;

    // CAN FD flags (linux/can.h)
    public const byte CANFD_BRS = 0x01; // bit rate switch
    public const byte CANFD_ESI = 0x02; // error state indicator

    // socket option
    public const int SO_TIMESTAMP = 29;
    public const int SO_RCVBUF = 8;
    public const int SO_SNDTIMEO = 21;
    public const int SO_TIMESTAMPNS = 35;
    public const int SO_TIMESTAMPING = 37;
    public const int SOF_TIMESTAMPING_SOFTWARE      = 1 << 4;
    public const int SOF_TIMESTAMPING_RX_HARDWARE   = 1 << 3;
    public const int SOF_TIMESTAMPING_TX_HARDWARE   = 1 << 0;
    public const int SOF_TIMESTAMPING_RAW_HARDWARE  = 1 << 6;
    public const int SCM_TIMESTAMPNS = SO_TIMESTAMPNS;
    public const int SCM_TIMESTAMPING = SO_TIMESTAMPING;

    public const int SIOCSHWTSTAMP = 0x89b0;


    public const int OK = 0;


    [StructLayout(LayoutKind.Sequential)]
    public struct sockaddr_can
    {
        public ushort can_family;
        public int can_ifindex;
        public uint rx_id;   // not used (CAN_J1939/BCM), padding keeps size
        public uint tx_id;   // not used
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
        public byte len;     // 0..64
        public byte flags;   // CANFD_BRS etc.
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
        public long tv_sec;   // seconds
        public long tv_nsec;  // nanoseconds
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
        public IntPtr data; // fd
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct timeval
    {
        public long tv_sec;
        public int  tv_usec;
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

    [DllImport("libc", SetLastError = true)]
    public static extern int socket(int domain, int type, int protocol);

    [DllImport("libc", SetLastError = true)]
    public static extern int bind(int sockfd, ref sockaddr_can addr, int addrlen);


    [DllImport("libc", SetLastError = true)]
    public static extern int connect(int sockfd, ref sockaddr_can addr, int addrlen);

    [DllImport("libc", SetLastError = true)]
    public static extern int fcntl(int fd, int cmd, int arg);

    [DllImport("libc", SetLastError = true)]
    public static extern int setsockopt(int sockfd, int level, int optname, ref int optval, uint optlen);

    [DllImport("libc", SetLastError = true)]
    public static unsafe extern int setsockopt(int sockfd, int level, int optname, void* optval, uint optlen);

    [DllImport("libc", SetLastError = true)]
    public static extern int ioctl(int fd, uint request, ref int argp);

    [DllImport("libc", SetLastError = true)]
    public static extern int ioctl(int fd, uint request, IntPtr argp);

    [DllImport("libc", SetLastError = true)]
    public static unsafe extern long read(int fd, void* buf, ulong count);

    [DllImport("libc", SetLastError = true)]
    public static unsafe extern long write(int fd, void* buf, ulong count);

    [DllImport("libc", SetLastError = true)]
    public static unsafe extern long recvmsg(int sockfd, msghdr* msg, int flags);

    [DllImport("libc", SetLastError = true)]
    public static unsafe extern int recvmmsg(int sockfd, mmsghdr* msgvec, uint vlen, int flags, timespec* timeout);

    [DllImport("libc", SetLastError = true)]
    public static unsafe extern int sendmmsg(int sockfd, mmsghdr* msgvec, uint vlen, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern int poll(ref pollfd fds, uint nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    public static extern int epoll_create1(int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int epoll_ctl(int epfd, int op, int fd, ref epoll_event ev);

    [DllImport("libc", SetLastError = true)]
    public static extern int epoll_wait(int epfd, [In, Out] epoll_event[] events, int maxevents, int timeout);

    [DllImport("libc", SetLastError = true)]
    public static extern int eventfd(uint initval, int flags);


    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern uint if_nametoindex(string ifname);


    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr if_indextoname(uint index, byte[] ifname);

     [DllImport("libc", CharSet = CharSet.Ansi, EntryPoint = "__xpg_strerror_r")]
    private static extern int __xpg_strerror_r(int errnum, StringBuilder buf, UIntPtr buflen);


    [DllImport("libc", CharSet = CharSet.Ansi, EntryPoint = "strerror_r")]
    private static extern int strerror_r_posix(int errnum, StringBuilder buf, UIntPtr buflen);


    [DllImport("libc", CharSet = CharSet.Ansi)]
    private static extern IntPtr strerror(int errnum);

    public static string StrError(int errno)
    {
        const int BUF = 256;
        var sb = new StringBuilder(BUF);

        try
        {
            if (__xpg_strerror_r(errno, sb, (UIntPtr)BUF) == 0)
                return sb.ToString();
        }
        catch (EntryPointNotFoundException)
        {
        }

        try
        {
            if (strerror_r_posix(errno, sb, (UIntPtr)BUF) == 0)
                return sb.ToString();
        }
        catch (EntryPointNotFoundException)
        {
        }

        return StrErrorFallBack(errno);
    }

    private static string StrErrorFallBack(int errno)
    {
        var p = strerror(errno);
        return p == IntPtr.Zero
            ? $"Unknown error {errno}"
            : Marshal.PtrToStringAnsi(p)!;
    }

    public static void ThrowErrno(string operation, string message)
    {
        var err = (uint)Marshal.GetLastWin32Error();
        throw new SocketCanNativeException(operation, message, err);
    }

    public static int Errno() => Marshal.GetLastWin32Error();

    public static void ThrowErrno(string operation, string message, int errno)
    {
        throw new SocketCanNativeException(operation, message, (uint)errno);
    }


    public static timeval ToTimeval(TimeSpan t)
    {
        return new timeval
        {
            tv_sec = (int)Math.Floor(t.TotalSeconds),
            tv_usec = (int)((t - TimeSpan.FromSeconds(Math.Floor(t.TotalSeconds))).TotalMilliseconds * 1000.0)
        };
    }
}
