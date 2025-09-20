using System;
using System.Runtime.InteropServices;

namespace Pkuyo.CanKit.SocketCAN.Native;

internal static class Libc
{
    // Address family / socket types
    public const int AF_CAN = 29;           // Linux specific
    public const int SOCK_RAW = 3;
    public const int CAN_RAW = 1;
    
    public const int SOL_SOCKET = 1;

    // SOL_CAN_RAW level and options
    public const int SOL_CAN_RAW = 101;
    public const int CAN_RAW_FILTER = 1;
    public const int CAN_RAW_ERR_FILTER = 2;
    public const int CAN_RAW_LOOPBACK = 3;
    public const int CAN_RAW_RECV_OWN_MSGS = 4;
    public const int CAN_RAW_FD_FRAMES = 5;

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

    // ioctls
    public const uint SIOCGIFINDEX = 0x8933; // ifreq.ifr_ifindex
    public const uint FIONREAD = 0x541B;     // bytes available

    // poll
    public const short POLLIN = 0x0001;
    public const short POLLOUT = 0x0004;
    
    // epoll
    public const int EPOLLIN = 0x001;
    public const int EPOLL_CTL_ADD = 1;
    
    // fcntl 
    public const int F_GETFL = 3;
    public const int F_SETFL = 4;
    public const int O_NONBLOCK = 0x800; 
    
    // CAN FD flags (linux/can.h)
    public const byte CANFD_BRS = 0x01; // bit rate switch
    public const byte CANFD_ESI = 0x02; // error state indicator
    
    // timeStamp
    public const int SO_TIMESTAMPING = 37;      
    public const int SOF_TIMESTAMPING_SOFTWARE      = 1 << 4;
    public const int SOF_TIMESTAMPING_RX_HARDWARE   = 1 << 3;
    public const int SOF_TIMESTAMPING_TX_HARDWARE   = 1 << 0;
    public const int SOF_TIMESTAMPING_RAW_HARDWARE  = 1 << 6;
    
    public const int SIOCSHWTSTAMP = 0x89b0;  
    
    
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct ifreq
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string ifr_name;
        public int ifr_ifindex;
    }

    [DllImport("libc", SetLastError = true)]
    public static extern int socket(int domain, int type, int protocol);

    [DllImport("libc", SetLastError = true)]
    public static extern int bind(int sockfd, ref sockaddr_can addr, int addrlen);
    
    [DllImport("libc", SetLastError = true)]
    public static extern int fcntl(int fd, int cmd, int arg);

    [DllImport("libc", SetLastError = true)]
    public static extern int setsockopt(int sockfd, int level, int optname, ref int optval, uint optlen);

    [DllImport("libc", SetLastError = true)]
    public static extern int setsockopt(int sockfd, int level, int optname, IntPtr optval, uint optlen);

    [DllImport("libc", SetLastError = true)]
    public static extern int ioctl(int fd, uint request, ref int argp);

    [DllImport("libc", SetLastError = true)]
    public static extern int ioctl(int fd, uint request, IntPtr argp);

    [DllImport("libc", SetLastError = true)]
    public static extern unsafe long read(int fd, void* buf, ulong count);

    [DllImport("libc", SetLastError = true)]
    public static extern unsafe long write(int fd, void* buf, ulong count);

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
    

    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern uint if_nametoindex(string ifname);
    
    public static void ThrowErrno(string where)
    {
        int err = Marshal.GetLastWin32Error();
        throw new InvalidOperationException($"{where} failed, errno={err}");
    }
}
