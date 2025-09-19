using System;
using System.Runtime.InteropServices;

namespace Pkuyo.CanKit.SocketCAN.Native;

internal static class Libc
{
    // Address family / socket types
    public const int AF_CAN = 29;           // Linux specific
    public const int SOCK_RAW = 3;
    public const int CAN_RAW = 1;

    // SOL_CAN_RAW level and options
    public const int SOL_CAN_RAW = 101;
    public const int CAN_RAW_FILTER = 1;
    public const int CAN_RAW_ERR_FILTER = 2;
    public const int CAN_RAW_LOOPBACK = 3;
    public const int CAN_RAW_RECV_OWN_MSGS = 4;
    public const int CAN_RAW_FD_FRAMES = 5;

    // ioctls
    public const uint SIOCGIFINDEX = 0x8933; // ifreq.ifr_ifindex
    public const uint FIONREAD = 0x541B;     // bytes available

    // poll
    public const short POLLIN = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    public struct sockaddr_can
    {
        public ushort can_family;
        public int can_ifindex;
        public uint rx_id;   // not used (CAN_J1939/BCM), padding keeps size
        public uint tx_id;   // not used
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct can_frame
    {
        public uint can_id;
        public byte can_dlc;
        public byte __pad;
        public byte __res0;
        public byte __res1;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct canfd_frame
    {
        public uint can_id;
        public byte len;     // 0..64
        public byte flags;   // CANFD_BRS etc.
        public byte __res0;
        public byte __res1;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] data;
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
    public static extern int setsockopt(int sockfd, int level, int optname, ref int optval, uint optlen);

    [DllImport("libc", SetLastError = true)]
    public static extern int setsockopt(int sockfd, int level, int optname, IntPtr optval, uint optlen);

    [DllImport("libc", SetLastError = true)]
    public static extern int ioctl(int fd, uint request, ref int argp);

    [DllImport("libc", SetLastError = true)]
    public static extern int ioctl(int fd, uint request, IntPtr argp);

    [DllImport("libc", SetLastError = true)]
    public static extern long read(int fd, IntPtr buf, ulong count);

    [DllImport("libc", SetLastError = true)]
    public static extern long write(int fd, IntPtr buf, ulong count);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern int poll(ref pollfd fds, uint nfds, int timeout);

    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern uint if_nametoindex(string ifname);
}

