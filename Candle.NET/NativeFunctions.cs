using System;
using System.Runtime.InteropServices;

namespace Candle {
    public class NativeFunctions {
        public enum candle_devstate_t {
            CANDLE_DEVSTATE_AVAIL,
            CANDLE_DEVSTATE_INUSE
        }

        public enum candle_frametype_t {
            CANDLE_FRAMETYPE_UNKNOWN,
            CANDLE_FRAMETYPE_RECEIVE,
            CANDLE_FRAMETYPE_ECHO,
            CANDLE_FRAMETYPE_ERROR,
            CANDLE_FRAMETYPE_TIMESTAMP_OVFL
        }

        [Flags]
        public enum candle_id_flags : UInt32 {
            CANDLE_ID_EXTENDED = 0x80000000,
            CANDLE_ID_RTR = 0x40000000,
            CANDLE_ID_ERR = 0x20000000
        }

        [Flags]
        public enum candle_mode_t : UInt32 {
            CANDLE_MODE_NORMAL = 0x00,
            CANDLE_MODE_LISTEN_ONLY = 0x01,
            CANDLE_MODE_LOOP_BACK = 0x02,
            CANDLE_MODE_TRIPLE_SAMPLE = 0x04,
            CANDLE_MODE_ONE_SHOT = 0x08,
            CANDLE_MODE_HW_TIMESTAMP = 0x10,
            CANDLE_MODE_PAD_PKTS_TO_MAX = 0x80,
            CANDLE_MODE_FD = 0x100,
            CANDLE_MODE_BERR_REPORTING = 0x1000,
            CANDLE_MODE_BUS_OFF_RECOVERY = 0x40000,
            CANDLE_MODE_AUTO_RESTART = 0x80000000,
        }

        public enum candle_err_t {
            CANDLE_ERR_OK = 0,
            CANDLE_ERR_CREATE_FILE = 1,
            CANDLE_ERR_WINUSB_INITIALIZE = 2,
            CANDLE_ERR_QUERY_INTERFACE = 3,
            CANDLE_ERR_QUERY_PIPE = 4,
            CANDLE_ERR_PARSE_IF_DESCR = 5,
            CANDLE_ERR_SET_HOST_FORMAT = 6,
            CANDLE_ERR_GET_DEVICE_INFO = 7,
            CANDLE_ERR_GET_BITTIMING_CONST = 8,
            CANDLE_ERR_PREPARE_READ = 9,
            CANDLE_ERR_SET_DEVICE_MODE = 10,
            CANDLE_ERR_SET_BITTIMING = 11,
            CANDLE_ERR_BITRATE_FCLK = 12,
            CANDLE_ERR_BITRATE_UNSUPPORTED = 13,
            CANDLE_ERR_SEND_FRAME = 14,
            CANDLE_ERR_READ_TIMEOUT = 15,
            CANDLE_ERR_READ_WAIT = 16,
            CANDLE_ERR_READ_RESULT = 17,
            CANDLE_ERR_READ_SIZE = 18,
            CANDLE_ERR_SETUPDI_IF_DETAILS = 19,
            CANDLE_ERR_SETUPDI_IF_DETAILS2 = 20,
            CANDLE_ERR_MALLOC = 21,
            CANDLE_ERR_PATH_LEN = 22,
            CANDLE_ERR_CLSID = 23,
            CANDLE_ERR_GET_DEVICES = 24,
            CANDLE_ERR_SETUPDI_IF_ENUM = 25,
            CANDLE_ERR_SET_TIMESTAMP_MODE = 26,
            CANDLE_ERR_DEV_OUT_OF_RANGE = 27,
            CANDLE_ERR_GET_TIMESTAMP = 28,
            CANDLE_ERR_SET_PIPE_RAW_IO = 29
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct candle_frame_t {
            public UInt32 echo_id;
            public UInt32 can_id;
            public byte can_dlc;
            public byte channel;
            public byte flags;
            public byte reserved;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] data;

            public UInt32 timestamp_us;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct candle_capability_t {
            public UInt32 feature;
            public UInt32 fclk_can;
            public UInt32 tseg1_min;
            public UInt32 tseg1_max;
            public UInt32 tseg2_min;
            public UInt32 tseg2_max;
            public UInt32 sjw_max;
            public UInt32 brp_min;
            public UInt32 brp_max;
            public UInt32 brp_inc;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct candle_bittiming_t {
            public UInt32 prop_seg;
            public UInt32 phase_seg1;
            public UInt32 phase_seg2;
            public UInt32 sjw;
            public UInt32 brp;
        }

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_list_scan(out IntPtr list);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_list_free(IntPtr list);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_list_length(IntPtr list, out byte length);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_dev_get(IntPtr list, byte dev_num, out IntPtr device);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_dev_get_state(IntPtr device, out candle_devstate_t deviceState);

        // The returned UTF-16 pointer belongs to the native device and must not be freed.
        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        public extern static IntPtr candle_dev_get_path(IntPtr device);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_dev_open(IntPtr device);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_dev_get_timestamp_us(IntPtr device, out UInt32 timestamp_us);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_dev_close(IntPtr device);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_dev_free(IntPtr device);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_channel_count(IntPtr device, out byte num_channels);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_channel_get_capabilities(IntPtr device, byte channel, out candle_capability_t cap);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_channel_set_timing(IntPtr device, byte channel, ref candle_bittiming_t data);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_channel_set_bitrate(IntPtr device, byte channel, UInt32 bitrate);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_channel_start(IntPtr device, byte channel, candle_mode_t flags);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_channel_stop(IntPtr device, byte channel);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_frame_send(IntPtr device, byte channel, ref candle_frame_t frame);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public extern static bool candle_frame_read(IntPtr device, out candle_frame_t frame, UInt32 timeout_ms);

        [DllImport("Candle.dll", CallingConvention = CallingConvention.StdCall)]
        public extern static candle_err_t candle_dev_last_error(IntPtr device);


        // Our functions
        public static void throwError(IntPtr device) {
            var error = candle_dev_last_error(device);
            throw (new Exception(error.ToString()));
        }
    }
}