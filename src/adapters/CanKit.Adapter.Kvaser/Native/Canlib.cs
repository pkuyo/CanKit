// Real Kvaser CANlib interop (disabled in FAKE builds)
#if !FAKE
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CanKit.Adapter.Kvaser.Native;

public static class Canlib
{
    public enum canStatus : int
    {
        canOK = 0,
        canERR_PARAM = -1,
        canERR_NOMSG = -2,
        canERR_NOTFOUND = -3,
        canERR_NOMEM = -4,
        canERR_NOCHANNELS = -5,
        canERR_INTERRUPTED = -6,
        canERR_TIMEOUT = -7,
        canERR_NOTINITIALIZED = -8,
        canERR_NOHANDLES = -9,
        canERR_INVHANDLE = -10,
        canERR_DRIVER = -12,
        canERR_TXBUFOFL = -13,
        canERR_HARDWARE = -15,
        canERR_DYNALOAD = -16,
        canERR_DYNALIB = -17,
        canERR_DYNAINIT = -18,
        canERR_NOT_SUPPORTED = -19,
        canERR_DRIVERLOAD = -23,
        canERR_DRIVERFAILED = -24,
        canERR_NOCARD = -26,
        canERR_REGISTRY = -28,
        canERR_INTERNAL = -30,
        canERR_NO_ACCESS = -31,
        canERR_NOT_IMPLEMENTED = -32,
    }

    // Open channel flags (subset)
    public const int canOPEN_ACCEPT_VIRTUAL = 0x0020;
    public const int canOPEN_CAN_FD = 0x0400;

    // Message flags
    public const int canMSG_RTR = 0x0001;
    public const int canMSG_STD = 0x0002;
    public const int canMSG_EXT = 0x0004;
    public const int canMSG_ERROR_FRAME = 0x0020;
    public const int canMSG_SINGLE_SHOT = 0x1000000;
    public const int canMSG_LOCAL_TXACK = 0x10000000;
    public const int canMSG_TXACK = 0x0040;

    // CAN FD message flags
    public const int canFDMSG_FDF = 0x010000;
    public const int canFDMSG_BRS = 0x020000;
    public const int canFDMSG_ESI = 0x040000;

    // Notification flags
    public const int canNOTIFY_RX = 0x0001;
    public const int canNOTIFY_ERROR = 0x0004;

    // Bus status flags
    public const int canSTAT_ERROR_PASSIVE = 0x00000001;
    public const int canSTAT_BUS_OFF = 0x00000002;
    public const int canSTAT_ERROR_WARNING = 0x00000004;
    public const int canSTAT_ERROR_ACTIVE = 0x00000008;

    // IOCTL codes (subset used)
    public const uint canIOCTL_FLUSH_RX_BUFFER = 10;
    public const uint canIOCTL_FLUSH_TX_BUFFER = 11;
    public const uint canIOCTL_SET_TIMER_SCALE = 6;
    public const uint canIOCTL_SET_RX_QUEUE_SIZE = 27;
    public const uint canIOCTL_SET_LOCAL_TXACK = 46;
    public const uint canIOCTL_SET_LOCAL_TXECHO = 32;
    public const uint canIOCTL_SET_TXACK = 7;

    // Predefined classic bitrates
    public const int canBITRATE_1M = -1;
    public const int canBITRATE_500K = -2;
    public const int canBITRATE_250K = -3;
    public const int canBITRATE_125K = -4;
    public const int canBITRATE_100K = -5;
    public const int canBITRATE_62K = -6;
    public const int canBITRATE_50K = -7;
    public const int canBITRATE_83K = -8;
    public const int canBITRATE_10K = -9;

    // Predefined FD bitrates (bps@sample-point)
    public const int canFD_BITRATE_500K_80P = -1000;
    public const int canFD_BITRATE_1M_80P = -1001;
    public const int canFD_BITRATE_2M_80P = -1002;
    public const int canFD_BITRATE_4M_80P = -1003;
    public const int canFD_BITRATE_8M_60P = -1004;
    public const int canFD_BITRATE_8M_80P = -1005;
    public const int canFD_BITRATE_8M_70P = -1006;
    public const int canFD_BITRATE_2M_60P = -1007;

    // Channel data item ids (subset)
    public const int canCHANNELDATA_CHANNEL_CAP = 1;
    public const int canCHANNELDATA_CARD_UPC_NO = 11;
    public const int canCHANNELDATA_CARD_SERIAL_NO = 7;
    public const int canCHANNELDATA_CHANNEL_NAME = 13;
    public const int canCHANNELDATA_CHANNEL_CAP_EX = 47;

    // Channel capability bits (subset)
    public const uint canCHANNEL_CAP_CAN_FD = 0x00080000;
    public const uint canCHANNEL_CAP_CAN_FD_NONISO = 0x00100000;
    public const uint canCHANNEL_CAP_SILENT_MODE = 0x00200000;
    public const uint canCHANNEL_CAP_ERROR_COUNTERS = 0x00000004;
    public const uint canCHANNEL_CAP_BUS_STATISTICS = 0x00000002;

    // filter
    public const uint canFILTER_SET_CODE_EXT = 5;
    public const uint canFILTER_SET_CODE_STD = 3;
    public const uint canFILTER_SET_MASK_EXT = 6;
    public const uint canFILTER_SET_MASK_STD = 4;
    // Object buffer types
    public enum canObjBufType : int
    {
        AUTO_RESPONSE = 0x01,
        PERIODIC_TX = 0x02
    }

    // Callback delegate for kvSetNotifyCallback (stdcall per header)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void kvCallbackDelegate(int hnd, IntPtr context, int notifyEvent);

    // DllImports (raw)
    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall)]
    public static extern void canInitializeLibrary();

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall)]
    public static extern canStatus canUnloadLibrary();

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall)]
    public static extern int canOpenChannel(int channel, int flags);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall)]
    public static extern canStatus canClose(int hnd);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canBusOn")]
    public static extern canStatus canBusOn(int hnd);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canBusOff")]
    public static extern canStatus canBusOff(int hnd);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canSetBusParams")]
    public static extern canStatus canSetBusParams(int hnd, int freq, int tseg1, int tseg2, int sjw, int noSamp, int syncmode);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canSetBusParamsFd")]
    public static extern canStatus canSetBusParamsFd(int hnd, int freq_brs, int tseg1_brs, int tseg2_brs, int sjw_brs);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canGetNumberOfChannels")]
    public static extern canStatus canGetNumberOfChannels(out int channelCount);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall)]
    public static extern canStatus canGetChannelData(int channel, int item, IntPtr buffer, UIntPtr bufsize);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canReadStatus")]
    public static extern canStatus canReadStatus(int hnd, out uint flags);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canReadErrorCounters")]
    public static extern canStatus canReadErrorCounters(int hnd, out uint txErr, out uint rxErr, out uint ovErr);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall)]
    public static unsafe extern canStatus canWrite(int hnd, int id, byte* msg, uint dlc, uint flag);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canRead")]
    public static extern canStatus canRead(int hnd, out int id, [Out] byte[] msg, out int dlc, out int flag, out uint time);

    // Bus usage/statistics
    [StructLayout(LayoutKind.Sequential)]
    public struct canBusStatistics
    {
        public uint stdData;
        public uint stdRemote;
        public uint extData;
        public uint extRemote;
        public uint errFrame;
        public uint busLoad;   // 0-10000 => 0.00% - 100.00%
        public uint overruns;
    }

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canRequestBusStatistics")]
    public static extern canStatus canRequestBusStatistics(int hnd);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canGetBusStatistics")]
    public static extern canStatus canGetBusStatistics(int hnd, out canBusStatistics stat, UIntPtr bufsiz);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern canStatus canGetErrorText(canStatus err, StringBuilder buf, uint bufsiz);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall)]
    public static extern canStatus canIoCtl(int hnd, uint func, IntPtr buf, uint buflen);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall)]
    public static extern canStatus canIoCtl(int hnd, uint func, ref int value, uint buflen);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall)]
    public static extern canStatus canIoCtl(int hnd, uint func, ref uint value, uint buflen);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall)]
    public static extern canStatus canIoCtl(int hnd, uint func, byte[] buffer, uint buflen);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern canStatus canIoCtl(int hnd, uint func, StringBuilder sb, uint buflen);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall)]
    public static extern canStatus canAccept(int hnd, int envelope, uint flag);

    public static canStatus canSetAcceptanceFilter(int hnd, uint code, uint mask, int is_extended)
    {
        var re = canAccept(hnd, (int)code, (uint)(canFILTER_SET_CODE_STD + is_extended));
        if (re != canStatus.canOK)
            return re;
        re = canAccept(hnd, (int)code, (uint)(canFILTER_SET_MASK_STD + is_extended));
        return re;
    }

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufAllocate")]
    public static extern canStatus canObjBufAllocate(int hnd, int type);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufFree")]
    public static extern canStatus canObjBufFree(int hnd, int idx);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufEnable")]
    public static extern canStatus canObjBufEnable(int hnd, int idx);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufDisable")]
    public static extern canStatus canObjBufDisable(int hnd, int idx);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufWrite")]
    public static extern canStatus canObjBufWrite(int hnd, int idx, int id, byte[] msg, uint dlc, uint flags);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufSetPeriod")]
    public static extern canStatus canObjBufSetPeriod(int hnd, int idx, uint periodUs);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "kvSetNotifyCallback")]
    public static extern canStatus kvSetNotifyCallback(int hnd, kvCallbackDelegate callback, IntPtr context, uint notifyFlags);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canGetChannelData")]
    public static extern canStatus canGetChannelData_UInt32(
        int channel, int item, out uint value, UIntPtr bufsize);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canGetChannelData")]
    public static extern canStatus canGetChannelData_UInt32Array(
        int channel, int item, uint[] buffer, UIntPtr bufsize);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall, EntryPoint = "canGetChannelData")]
    public static extern canStatus canGetChannelData_UInt64Array(
        int channel, int item, ulong[] buffer, UIntPtr bufsize);

    // --- 字符串版本（ANSI / Unicode / UTF-8）---
    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, EntryPoint = "canGetChannelData")]
    public static extern canStatus canGetChannelData_Ansi(
        int channel, int item, StringBuilder buffer, UIntPtr bufsize);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Unicode, EntryPoint = "canGetChannelData")]
    public static extern canStatus canGetChannelData_Wide(
        int channel, int item, StringBuilder buffer, UIntPtr bufsize);

    [DllImport("canlib32", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "canGetChannelData")]
    public static extern canStatus canGetChannelData_Bytes(
        int channel, int item, byte[] buffer, UIntPtr bufsize);
    public static canStatus canGetErrorText(canStatus err, out string msg)
    {
        var sb = new StringBuilder(256);
        var re = canGetErrorText(err, sb, (uint)sb.Capacity);
        msg = sb.ToString();
        return re;
    }

    public static canStatus GetChannelName(int channel, out string name)
    {
        name = string.Empty;
        var sb = new StringBuilder(256);
        var st = canGetChannelData_Ansi(channel, canCHANNELDATA_CHANNEL_NAME, sb, (UIntPtr)(uint)sb.Capacity);
        if (st == canStatus.canOK) name = sb.ToString();
        return st;
    }
    ///  CHANNEL_CAP / TRANS_CAP / BUS_TYPE...
    public static canStatus GetUInt32(int channel, int item, out uint value)
    {
        var st = canGetChannelData_UInt32(channel, item, out value, (UIntPtr)4u);
        return st;
    }

    /// CARD_SERIAL_NO / TRANS_SERIAL_NO / CARD_FIRMWARE_REV / CARD_HARDWARE_REV / CARD_UPC_NO / TRANS_UPC_NO...
    public static canStatus GetUInt32Pair(int channel, int item, out uint hi, out uint lo)
    {
        var arr = new uint[2];
        var st = canGetChannelData_UInt32Array(channel, item, arr, (UIntPtr)(2 * sizeof(uint)));
        hi = arr[1];
        lo = arr[0];
        return st;
    }

    public static canStatus GetChannelCapEx(int channel, out ulong word0, out ulong word1)
    {
        var arr = new ulong[2];
        var st = canGetChannelData_UInt64Array(channel, canCHANNELDATA_CHANNEL_CAP, arr, (UIntPtr)(2 * sizeof(ulong)));
        word0 = arr[0];
        word1 = arr.Length > 1 ? arr[1] : 0;
        return st;
    }
    // CARD_UPC_NO / TRANS_UPC_NO
    public static canStatus GetEanString(int channel, int item, out string ean)
    {
        ean = string.Empty;
        var st = GetUInt32Pair(channel, item, out uint hi, out uint lo);
        if (st != canStatus.canOK) return st;

        ulong v = ((ulong)hi << 32) | lo;
        string digits = v.ToString("D13");
        ean = $"{digits.Substring(0, 2)}-{digits.Substring(2, 5)}-{digits.Substring(7, 5)}-{digits.Substring(12, 1)}";
        return st;
    }
}
#endif
