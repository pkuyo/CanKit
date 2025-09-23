using System.Runtime.InteropServices;
#nullable disable
#pragma warning disable IDE0055
#pragma warning disable CS8981

namespace Pkuyo.CanKit.Net.SocketCAN.Native
{
    internal class LibSocketCan
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct can_bittiming
        {
            public UInt32 bitrate;         /* Bit-rate in bits/second */
            public UInt32 sample_point;    /* Sample point in one-tenth of a percent */
            public UInt32 tq;              /* Time quanta (TQ) in nanoseconds */
            public UInt32 prop_seg;        /* Propagation segment in TQs */
            public UInt32 phase_seg1;      /* Phase buffer segment 1 in TQs */
            public UInt32 phase_seg2;      /* Phase buffer segment 2 in TQs */
            public UInt32 sjw;             /* Synchronisation jump width in TQs */
            public UInt32 brp;             /* Bit-rate prescaler */
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct  can_bittiming_const
        {
            public fixed byte name[16];         /* Name of the CAN controller hardware */
            public UInt32 tseg1_min;            /* Time segement 1 = prop_seg + phase_seg1 */
            public UInt32 tseg1_max;
            public UInt32 tseg2_min;            /* Time segement 2 = phase_seg2 */
            public UInt32 tseg2_max;
            public UInt32 sjw_max;              /* Synchronisation jump width */
            public UInt32 brp_min;              /* Bit-rate prescaler */
            public UInt32 brp_max;
            public UInt32 brp_inc;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct can_clock
        {
            public UInt32 freq;		/* CAN system clock frequency in Hz */
        }

        public enum can_state
        {
            CAN_STATE_ERROR_ACTIVE = 0, /* RX/TX error count < 96 */
            CAN_STATE_ERROR_WARNING,    /* RX/TX error count < 128 */
            CAN_STATE_ERROR_PASSIVE,    /* RX/TX error count < 256 */
            CAN_STATE_BUS_OFF,          /* RX/TX error count >= 256 */
            CAN_STATE_STOPPED,          /* Device is stopped */
            CAN_STATE_SLEEPING,         /* Device is sleeping */
            CAN_STATE_MAX
        }

        public struct can_berr_counter
        {
            public UInt16 txerr;
            public UInt16 rxerr;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct can_ctrlmode
        {
            public UInt32 mask;
            public UInt32 flags;
        }

        public const UInt32 CAN_CTRLMODE_LOOPBACK = 0x01;	        /* Loopback mode */
        public const UInt32 CAN_CTRLMODE_LISTENONLY = 0x02;	        /* Listen-only mode */
        public const UInt32 CAN_CTRLMODE_3_SAMPLES = 0x04;	        /* Triple sampling mode */
        public const UInt32 CAN_CTRLMODE_ONE_SHOT = 0x08;	        /* One-Shot mode */
        public const UInt32 CAN_CTRLMODE_BERR_REPORTING = 0x10;	    /* Bus-error reporting */
        public const UInt32 CAN_CTRLMODE_FD = 0x20;	                /* CAN FD mode */
        public const UInt32 CAN_CTRLMODE_PRESUME_ACK = 0x40;        /* Ignore missing CAN ACKs */

        [StructLayout(LayoutKind.Sequential)]
        public struct can_device_stats
        {
            public UInt32 bus_error;            /* Bus errors */
            public UInt32 error_warning;        /* Changes to error warning state */
            public UInt32 error_passive;        /* Changes to error passive state */
            public UInt32 bus_off;              /* Changes to bus off state */
            public UInt32 arbitration_lost;     /* Arbitration lost errors */
            public UInt32 restarts;             /* CAN controller re-starts */
        };

        public enum IFLA_CAN : UInt32
        {
	        IFLA_CAN_UNSPEC,
	        IFLA_CAN_BITTIMING,
	        IFLA_CAN_BITTIMING_CONST,
	        IFLA_CAN_CLOCK,
	        IFLA_CAN_STATE,
	        IFLA_CAN_CTRLMODE,
	        IFLA_CAN_RESTART_MS,
	        IFLA_CAN_RESTART,
	        IFLA_CAN_BERR_COUNTER,
	        IFLA_CAN_DATA_BITTIMING,
	        IFLA_CAN_DATA_BITTIMING_CONST,
	        __IFLA_CAN_MAX
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct rtnl_link_stats64
        {
            public UInt64 rx_packets;
            public UInt64 tx_packets;
            public UInt64 rx_bytes;
            public UInt64 tx_bytes;
            public UInt64 rx_errors;
            public UInt64 tx_errors;
            public UInt64 rx_dropped;
            public UInt64 tx_dropped;
            public UInt64 multicast;
            public UInt64 collisions;
            public UInt64 rx_length_errors;
            public UInt64 rx_over_errors;
            public UInt64 rx_crc_errors;
            public UInt64 rx_frame_errors;
            public UInt64 rx_fifo_errors;
            public UInt64 rx_missed_errors;
            public UInt64 tx_aborted_errors;
            public UInt64 tx_carrier_errors;
            public UInt64 tx_fifo_errors;
            public UInt64 tx_heartbeat_errors;
            public UInt64 tx_window_errors;
            public UInt64 rx_compressed;
            public UInt64 tx_compressed;
            public UInt64 rx_nohandler;
            public UInt64 rx_otherhost_dropped;
        };


        public const UInt32 IFLA_CAN_MAX = ((UInt32)IFLA_CAN.__IFLA_CAN_MAX - 1);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_do_restart(string name);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_do_stop(string name);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_do_start(string name);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_set_restart_ms(string name, UInt32 restart_ms);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_set_bittiming(string name, in can_bittiming bt);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_set_canfd_bittiming(string name, in can_bittiming bt, in can_bittiming dbt);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_set_fd_bitrates(string name, UInt32 bitrate, UInt32 dbitrate);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_set_ctrlmode(string name, in can_ctrlmode cm);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_set_bitrate(string name, UInt32 bitrate);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_set_bitrate_samplepoint(string name, UInt32 bitrate, UInt32 sample_point);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_get_restart_ms(string name, out UInt32 restart_ms);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_get_bittiming(string name, out can_bittiming bt);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_get_ctrlmode(string name, out can_ctrlmode cm);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_get_state(string name, out int state);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_get_clock(string name, out can_clock clock);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_get_bittiming_const(string name, out can_bittiming_const btc);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_get_berr_counter(string name, out can_berr_counter bc);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int can_get_device_stats(string name, out can_device_stats cds);

        [DllImport("libsocket.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern unsafe int can_get_link_stats(string name, out rtnl_link_stats64 rls);
    }
}
