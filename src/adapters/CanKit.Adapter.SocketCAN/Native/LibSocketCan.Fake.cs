// FAKE libsocketcan backend for unit tests
#if FAKE

using System;
using System.Runtime.InteropServices;
// @formatter:off
#nullable disable
#pragma warning disable IDE0055
#pragma warning disable CS8981
namespace CanKit.Adapter.SocketCAN.Native
{
    internal class LibSocketCan
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct can_bittiming
        {
            public UInt32 bitrate;
            public UInt32 sample_point;
            public UInt32 tq;
            public UInt32 prop_seg;
            public UInt32 phase_seg1;
            public UInt32 phase_seg2;
            public UInt32 sjw;
            public UInt32 brp;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct can_bittiming_const
        {
            public fixed byte name[16];
            public UInt32 tseg1_min;
            public UInt32 tseg1_max;
            public UInt32 tseg2_min;
            public UInt32 tseg2_max;
            public UInt32 sjw_max;
            public UInt32 brp_min;
            public UInt32 brp_max;
            public UInt32 brp_inc;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct can_clock { public UInt32 freq; }

        public enum can_state
        {
            CAN_STATE_ERROR_ACTIVE = 0,
            CAN_STATE_ERROR_WARNING,
            CAN_STATE_ERROR_PASSIVE,
            CAN_STATE_BUS_OFF,
            CAN_STATE_STOPPED,
            CAN_STATE_SLEEPING,
            CAN_STATE_MAX
        }

        public struct can_berr_counter { public UInt16 txerr; public UInt16 rxerr; }

        [StructLayout(LayoutKind.Sequential)]
        public struct can_ctrlmode { public UInt32 mask; public UInt32 flags; }

        public const UInt32 CAN_CTRLMODE_LOOPBACK = 0x01;
        public const UInt32 CAN_CTRLMODE_LISTENONLY = 0x02;
        public const UInt32 CAN_CTRLMODE_3_SAMPLES = 0x04;
        public const UInt32 CAN_CTRLMODE_ONE_SHOT = 0x08;
        public const UInt32 CAN_CTRLMODE_BERR_REPORTING = 0x10;
        public const UInt32 CAN_CTRLMODE_FD = 0x20;
        public const UInt32 CAN_CTRLMODE_PRESUME_ACK = 0x40;

        [StructLayout(LayoutKind.Sequential)]
        public struct can_device_stats
        {
            public UInt32 bus_error, error_warning, error_passive, bus_off, arbitration_lost, restarts;
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

        public const uint IFLA_CAN_MAX = ((uint)IFLA_CAN.__IFLA_CAN_MAX - 1);

        // Fake state store for vcan0/1/2
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IfaceState> _state = new(StringComparer.Ordinal);

        private sealed class IfaceState
        {
            public uint Bitrate = 500_000;
            public uint DataBitrate = 500_000;
            public uint Clock = 80_000_000;
            public can_state State = can_state.CAN_STATE_STOPPED;
            public UInt32 CtrlMask = CAN_CTRLMODE_LOOPBACK | CAN_CTRLMODE_LISTENONLY | CAN_CTRLMODE_FD | CAN_CTRLMODE_BERR_REPORTING;
            public UInt32 CtrlFlags = 0;
        }

        private static IfaceState Get(string name) => _state.GetOrAdd(name, _ => new IfaceState());

        public static int can_do_restart(string name) { Get(name).State = can_state.CAN_STATE_ERROR_ACTIVE; return 0; }
        public static int can_do_stop(string name) { Get(name).State = can_state.CAN_STATE_STOPPED; return 0; }
        public static int can_do_start(string name) { Get(name).State = can_state.CAN_STATE_ERROR_ACTIVE; return 0; }
        public static int can_set_restart_ms(string name, UInt32 restart_ms) => 0;

        public static int can_set_bittiming(string name, in can_bittiming bt)
        { var s = Get(name); s.Bitrate = bt.bitrate; return 0; }

        public static int can_set_canfd_bittiming(string name, in can_bittiming bt, in can_bittiming dbt)
        { var s = Get(name); s.Bitrate = bt.bitrate; s.DataBitrate = dbt.bitrate; return 0; }

        public static int can_set_ctrlmode(string name, in can_ctrlmode cm)
        { var s = Get(name); s.CtrlFlags = cm.flags; return 0; }

        public static int can_set_bitrate(string name, UInt32 bitrate)
        { var s = Get(name); s.Bitrate = bitrate; return 0; }

        public static int can_set_bitrate_samplepoint(string name, UInt32 bitrate, UInt32 sample_point)
        { var s = Get(name); s.Bitrate = bitrate; return 0; }

        public static int can_get_restart_ms(string name, out UInt32 restart_ms) { restart_ms = 0; return 0; }

        public static int can_get_bittiming(string name, out can_bittiming bt)
        {
            var s = Get(name);
            bt = new can_bittiming { bitrate = s.Bitrate };
            return 0;
        }

        public static int can_get_ctrlmode(string name, out can_ctrlmode cm)
        {
            var s = Get(name);
            cm = new can_ctrlmode { mask = s.CtrlMask, flags = s.CtrlFlags };
            return 0;
        }

        public static int can_get_state(string name, out int state)
        { state = (int)Get(name).State; return 0; }

        public static int can_get_clock(string name, out can_clock clock)
        { var s = Get(name); clock = new can_clock { freq = s.Clock }; return 0; }

        public static int can_get_bittiming_const(string name, out can_bittiming_const btc)
        {
            btc = default; // not used in tests
            return 0;
        }

        //TODO: Inject error and error counters
        public static int can_get_berr_counter(string name, out can_berr_counter bc)
        { bc = new can_berr_counter(){ txerr = 0, rxerr = 0}; return 0; }

        public static int can_get_device_stats(string name, out can_device_stats cds)
        { cds = default; return 0; }

        public static int can_get_link_stats(string name, out rtnl_link_stats64 rls)
        { rls = default; return 0; }

        [StructLayout(LayoutKind.Sequential)]
        public struct rtnl_link_stats64 { public UInt64 rx_packets, tx_packets, rx_bytes, tx_bytes, rx_errors, tx_errors, rx_dropped, tx_dropped, multicast, collisions, rx_length_errors, rx_over_errors, rx_crc_errors, rx_frame_errors, rx_fifo_errors, rx_missed_errors, tx_aborted_errors, tx_carrier_errors, tx_fifo_errors, tx_heartbeat_errors, tx_window_errors, rx_compressed, tx_compressed, rx_nohandler, rx_otherhost_dropped; }
    }
}
#endif

