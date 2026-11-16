using System;
using System.Runtime.InteropServices;

namespace CanKit.Adapter.Candlelight.Native;

internal static class CandleNative
{
    internal const uint ExtendedId = 0x80000000;
    internal const uint RemoteRequest = 0x40000000;
    internal const uint ErrorId = 0x20000000;
    internal const int ReadTimeout = 15;

    [Flags]
    internal enum Mode : uint
    {
        Normal = 0,
        ListenOnly = 1,
        LoopBack = 2,
        TripleSample = 4,
        OneShot = 8,
        HardwareTimestamp = 0x10,
        BusErrorReporting = 0x1000
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Frame
    {
        public uint EchoId;
        public uint CanId;
        public byte Dlc;
        public byte Channel;
        public byte Flags;
        public byte Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Data;
        public uint TimestampMicroseconds;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Capabilities
    {
        public uint Feature;
        public uint Clock;
        public uint Tseg1Min;
        public uint Tseg1Max;
        public uint Tseg2Min;
        public uint Tseg2Max;
        public uint SjwMax;
        public uint BrpMin;
        public uint BrpMax;
        public uint BrpIncrement;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BitTiming
    {
        public uint PropSeg;
        public uint PhaseSeg1;
        public uint PhaseSeg2;
        public uint Sjw;
        public uint Brp;
    }

    private const string Library = "Candle.dll";

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_list_scan(out IntPtr list);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_list_free(IntPtr list);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_list_length(IntPtr list, out byte length);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_dev_get(IntPtr list, byte index, out IntPtr device);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_dev_get_state(IntPtr device, out int state);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern IntPtr candle_dev_get_path(IntPtr device);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_dev_open(IntPtr device);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_dev_close(IntPtr device);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_dev_free(IntPtr device);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_channel_count(IntPtr device, out byte count);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_channel_get_capabilities(IntPtr device, byte channel, out Capabilities capabilities);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_channel_set_timing(IntPtr device, byte channel, ref BitTiming timing);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_channel_start(IntPtr device, byte channel, Mode mode);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_channel_stop(IntPtr device, byte channel);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_frame_send(IntPtr device, byte channel, ref Frame frame);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool candle_frame_read(IntPtr device, out Frame frame, uint timeoutMilliseconds);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int candle_dev_last_error(IntPtr device);
}
