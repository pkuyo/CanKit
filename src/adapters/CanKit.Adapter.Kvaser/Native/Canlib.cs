// Real Kvaser CANlib interop (disabled in FAKE builds)
#if !FAKE
using System;
using System.Collections.Concurrent;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxCanBusStatistics
    {
        public nuint stdData;
        public nuint stdRemote;
        public nuint extData;
        public nuint extRemote;
        public nuint errFrame;
        public nuint busLoad;
        public nuint overruns;

        public canBusStatistics ToPublic() => new()
        {
            stdData = (uint)stdData,
            stdRemote = (uint)stdRemote,
            extData = (uint)extData,
            extRemote = (uint)extRemote,
            errFrame = (uint)errFrame,
            busLoad = (uint)busLoad,
            overruns = (uint)overruns,
        };
    }

    public delegate void kvCallbackDelegate(int hnd, IntPtr context, int notifyEvent);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void kvCallbackDelegateStdCall(int hnd, IntPtr context, int notifyEvent);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void kvCallbackDelegateCdecl(int hnd, IntPtr context, int notifyEvent);

    private static readonly ConcurrentDictionary<int, Delegate> NotifyThunks = new();

    public static void canInitializeLibrary()
    {
        if (KvaserNativeLibraries.IsLinux)
            LinuxAbi.canInitializeLibrary();
        else
            WindowsAbi.canInitializeLibrary();
    }

    public static canStatus canUnloadLibrary()
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canUnloadLibrary()
            : WindowsAbi.canUnloadLibrary();
    }

    public static int canOpenChannel(int channel, int flags)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canOpenChannel(channel, flags)
            : WindowsAbi.canOpenChannel(channel, flags);
    }

    public static canStatus canClose(int hnd)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canClose(hnd)
            : WindowsAbi.canClose(hnd);
    }

    public static canStatus canBusOn(int hnd)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canBusOn(hnd)
            : WindowsAbi.canBusOn(hnd);
    }

    public static canStatus canBusOff(int hnd)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canBusOff(hnd)
            : WindowsAbi.canBusOff(hnd);
    }

    public static canStatus canSetBusParams(int hnd, int freq, int tseg1, int tseg2, int sjw, int noSamp, int syncmode)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canSetBusParams(hnd, freq, tseg1, tseg2, sjw, noSamp, syncmode)
            : WindowsAbi.canSetBusParams(hnd, freq, tseg1, tseg2, sjw, noSamp, syncmode);
    }

    public static canStatus canSetBusParamsFd(int hnd, int freq_brs, int tseg1_brs, int tseg2_brs, int sjw_brs)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canSetBusParamsFd(hnd, freq_brs, tseg1_brs, tseg2_brs, sjw_brs)
            : WindowsAbi.canSetBusParamsFd(hnd, freq_brs, tseg1_brs, tseg2_brs, sjw_brs);
    }

    public static canStatus canGetNumberOfChannels(out int channelCount)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canGetNumberOfChannels(out channelCount)
            : WindowsAbi.canGetNumberOfChannels(out channelCount);
    }

    public static canStatus canGetChannelData(int channel, int item, IntPtr buffer, UIntPtr bufsize)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canGetChannelData(channel, item, buffer, bufsize)
            : WindowsAbi.canGetChannelData(channel, item, buffer, bufsize);
    }

    public static canStatus canReadStatus(int hnd, out uint flags)
    {
        if (KvaserNativeLibraries.IsLinux)
        {
            var status = LinuxAbi.canReadStatus(hnd, out nuint nativeFlags);
            flags = (uint)nativeFlags;
            return status;
        }

        return WindowsAbi.canReadStatus(hnd, out flags);
    }

    public static canStatus canReadErrorCounters(int hnd, out uint txErr, out uint rxErr, out uint ovErr)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canReadErrorCounters(hnd, out txErr, out rxErr, out ovErr)
            : WindowsAbi.canReadErrorCounters(hnd, out txErr, out rxErr, out ovErr);
    }

    public static unsafe canStatus canWrite(int hnd, int id, byte* msg, uint dlc, uint flag)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canWrite(hnd, id, msg, dlc, flag)
            : WindowsAbi.canWrite(hnd, id, msg, dlc, flag);
    }

    public static canStatus canRead(int hnd, out int id, [Out] byte[] msg, out int dlc, out int flag, out uint time)
    {
        if (KvaserNativeLibraries.IsLinux)
        {
            var status = LinuxAbi.canRead(hnd, out nint nativeId, msg, out dlc, out flag, out nuint nativeTime);
            id = (int)nativeId;
            time = (uint)nativeTime;
            return status;
        }

        return WindowsAbi.canRead(hnd, out id, msg, out dlc, out flag, out time);
    }

    public static canStatus canRequestBusStatistics(int hnd)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canRequestBusStatistics(hnd)
            : WindowsAbi.canRequestBusStatistics(hnd);
    }

    public static canStatus canGetBusStatistics(int hnd, out canBusStatistics stat, UIntPtr bufsiz)
    {
        if (KvaserNativeLibraries.IsLinux)
        {
            var status = LinuxAbi.canGetBusStatistics(
                hnd,
                out LinuxCanBusStatistics native,
                (UIntPtr)(uint)Marshal.SizeOf<LinuxCanBusStatistics>());
            stat = native.ToPublic();
            return status;
        }

        return WindowsAbi.canGetBusStatistics(hnd, out stat, bufsiz);
    }

    public static canStatus canGetErrorText(canStatus err, StringBuilder buf, uint bufsiz)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canGetErrorText(err, buf, bufsiz)
            : WindowsAbi.canGetErrorText(err, buf, bufsiz);
    }

    public static canStatus canIoCtl(int hnd, uint func, IntPtr buf, uint buflen)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canIoCtl(hnd, func, buf, buflen)
            : WindowsAbi.canIoCtl(hnd, func, buf, buflen);
    }

    public static canStatus canIoCtl(int hnd, uint func, ref int value, uint buflen)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canIoCtl_int(hnd, func, ref value, buflen)
            : WindowsAbi.canIoCtl_int(hnd, func, ref value, buflen);
    }

    public static canStatus canIoCtl(int hnd, uint func, ref uint value, uint buflen)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canIoCtl_uint(hnd, func, ref value, buflen)
            : WindowsAbi.canIoCtl_uint(hnd, func, ref value, buflen);
    }

    public static canStatus canIoCtl(int hnd, uint func, byte[] buffer, uint buflen)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canIoCtl_bytes(hnd, func, buffer, buflen)
            : WindowsAbi.canIoCtl_bytes(hnd, func, buffer, buflen);
    }

    public static canStatus canIoCtl(int hnd, uint func, StringBuilder sb, uint buflen)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canIoCtl_sb(hnd, func, sb, buflen)
            : WindowsAbi.canIoCtl_sb(hnd, func, sb, buflen);
    }

    public static canStatus canAccept(int hnd, int envelope, uint flag)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canAccept(hnd, envelope, flag)
            : WindowsAbi.canAccept(hnd, envelope, flag);
    }

    public static canStatus canObjBufAllocate(int hnd, int type)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canObjBufAllocate(hnd, type)
            : WindowsAbi.canObjBufAllocate(hnd, type);
    }

    public static canStatus canObjBufFree(int hnd, int idx)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canObjBufFree(hnd, idx)
            : WindowsAbi.canObjBufFree(hnd, idx);
    }

    public static canStatus canObjBufEnable(int hnd, int idx)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canObjBufEnable(hnd, idx)
            : WindowsAbi.canObjBufEnable(hnd, idx);
    }

    public static canStatus canObjBufDisable(int hnd, int idx)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canObjBufDisable(hnd, idx)
            : WindowsAbi.canObjBufDisable(hnd, idx);
    }

    public static canStatus canObjBufWrite(int hnd, int idx, int id, byte[] msg, uint dlc, uint flags)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canObjBufWrite(hnd, idx, id, msg, dlc, flags)
            : WindowsAbi.canObjBufWrite(hnd, idx, id, msg, dlc, flags);
    }

    public static canStatus canObjBufSetPeriod(int hnd, int idx, uint periodUs)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canObjBufSetPeriod(hnd, idx, periodUs)
            : WindowsAbi.canObjBufSetPeriod(hnd, idx, periodUs);
    }

    public static canStatus canGetChannelData_UInt32(int channel, int item, out uint value, UIntPtr bufsize)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canGetChannelData_UInt32(channel, item, out value, bufsize)
            : WindowsAbi.canGetChannelData_UInt32(channel, item, out value, bufsize);
    }

    public static canStatus canGetChannelData_UInt32Array(int channel, int item, uint[] buffer, UIntPtr bufsize)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canGetChannelData_UInt32Array(channel, item, buffer, bufsize)
            : WindowsAbi.canGetChannelData_UInt32Array(channel, item, buffer, bufsize);
    }

    public static canStatus canGetChannelData_UInt64Array(int channel, int item, ulong[] buffer, UIntPtr bufsize)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canGetChannelData_UInt64Array(channel, item, buffer, bufsize)
            : WindowsAbi.canGetChannelData_UInt64Array(channel, item, buffer, bufsize);
    }

    public static canStatus canGetChannelData_Ansi(int channel, int item, StringBuilder buffer, UIntPtr bufsize)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canGetChannelData_Ansi(channel, item, buffer, bufsize)
            : WindowsAbi.canGetChannelData_Ansi(channel, item, buffer, bufsize);
    }

    public static canStatus canGetChannelData_Wide(int channel, int item, StringBuilder buffer, UIntPtr bufsize)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canGetChannelData_Wide(channel, item, buffer, bufsize)
            : WindowsAbi.canGetChannelData_Wide(channel, item, buffer, bufsize);
    }

    public static canStatus canGetChannelData_Bytes(int channel, int item, byte[] buffer, UIntPtr bufsize)
    {
        return KvaserNativeLibraries.IsLinux
            ? LinuxAbi.canGetChannelData_Bytes(channel, item, buffer, bufsize)
            : WindowsAbi.canGetChannelData_Bytes(channel, item, buffer, bufsize);
    }

    public static canStatus kvSetNotifyCallback(int hnd, kvCallbackDelegate callback, IntPtr context, uint notifyFlags)
    {
        if (callback is null || notifyFlags == 0)
        {
            canStatus status = KvaserNativeLibraries.IsLinux
                ? LinuxAbi.kvSetNotifyCallback(hnd, null, context, 0)
                : WindowsAbi.kvSetNotifyCallback(hnd, null, context, 0);
            NotifyThunks.TryRemove(hnd, out _);
            return status;
        }

        if (KvaserNativeLibraries.IsLinux)
        {
            kvCallbackDelegateCdecl thunk = callback.Invoke;
            var status = LinuxAbi.kvSetNotifyCallback(hnd, thunk, context, notifyFlags);
            if (status == canStatus.canOK)
                NotifyThunks[hnd] = thunk;
            else
                NotifyThunks.TryRemove(hnd, out _);
            return status;
        }

        kvCallbackDelegateStdCall stdcall = callback.Invoke;
        var winStatus = WindowsAbi.kvSetNotifyCallback(hnd, stdcall, context, notifyFlags);
        if (winStatus == canStatus.canOK)
            NotifyThunks[hnd] = stdcall;
        else
            NotifyThunks.TryRemove(hnd, out _);
        return winStatus;
    }


    private static class WindowsAbi
    {
        private const string Dll = KvaserNativeLibraries.WindowsLibraryName;


        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern void canInitializeLibrary();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern canStatus canUnloadLibrary();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int canOpenChannel(int channel, int flags);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern canStatus canClose(int hnd);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canBusOn")]
        public static extern canStatus canBusOn(int hnd);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canBusOff")]
        public static extern canStatus canBusOff(int hnd);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canSetBusParams")]
        public static extern canStatus canSetBusParams(int hnd, int freq, int tseg1, int tseg2, int sjw, int noSamp, int syncmode);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canSetBusParamsFd")]
        public static extern canStatus canSetBusParamsFd(int hnd, int freq_brs, int tseg1_brs, int tseg2_brs, int sjw_brs);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canGetNumberOfChannels")]
        public static extern canStatus canGetNumberOfChannels(out int channelCount);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern canStatus canGetChannelData(int channel, int item, IntPtr buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canReadStatus")]
        public static extern canStatus canReadStatus(int hnd, out uint flags);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canReadErrorCounters")]
        public static extern canStatus canReadErrorCounters(int hnd, out uint txErr, out uint rxErr, out uint ovErr);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static unsafe extern canStatus canWrite(int hnd, int id, byte* msg, uint dlc, uint flag);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canRead")]
        public static extern canStatus canRead(int hnd, out int id, [Out] byte[] msg, out int dlc, out int flag, out uint time);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canRequestBusStatistics")]
        public static extern canStatus canRequestBusStatistics(int hnd);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canGetBusStatistics")]
        public static extern canStatus canGetBusStatistics(int hnd, out canBusStatistics stat, UIntPtr bufsiz);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern canStatus canGetErrorText(canStatus err, StringBuilder buf, uint bufsiz);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern canStatus canIoCtl(int hnd, uint func, IntPtr buf, uint buflen);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canIoCtl")]
        public static extern canStatus canIoCtl_int(int hnd, uint func, ref int value, uint buflen);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canIoCtl")]
        public static extern canStatus canIoCtl_uint(int hnd, uint func, ref uint value, uint buflen);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canIoCtl")]
        public static extern canStatus canIoCtl_bytes(int hnd, uint func, byte[] buffer, uint buflen);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "canIoCtl")]
        public static extern canStatus canIoCtl_sb(int hnd, uint func, StringBuilder sb, uint buflen);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern canStatus canAccept(int hnd, int envelope, uint flag);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufAllocate")]
        public static extern canStatus canObjBufAllocate(int hnd, int type);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufFree")]
        public static extern canStatus canObjBufFree(int hnd, int idx);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufEnable")]
        public static extern canStatus canObjBufEnable(int hnd, int idx);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufDisable")]
        public static extern canStatus canObjBufDisable(int hnd, int idx);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufWrite")]
        public static extern canStatus canObjBufWrite(int hnd, int idx, int id, byte[] msg, uint dlc, uint flags);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canObjBufSetPeriod")]
        public static extern canStatus canObjBufSetPeriod(int hnd, int idx, uint periodUs);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_UInt32(int channel, int item, out uint value, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_UInt32Array(int channel, int item, uint[] buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_UInt64Array(int channel, int item, ulong[] buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_Ansi(int channel, int item, StringBuilder buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_Wide(int channel, int item, StringBuilder buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_Bytes(int channel, int item, byte[] buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, EntryPoint = "kvSetNotifyCallback")]
        public static extern canStatus kvSetNotifyCallback(int hnd, kvCallbackDelegateStdCall? callback, IntPtr context, uint notifyFlags);

    }

    private static class LinuxAbi
    {
        private const string Dll = KvaserNativeLibraries.LinuxLibraryName;


        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void canInitializeLibrary();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern canStatus canUnloadLibrary();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int canOpenChannel(int channel, int flags);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern canStatus canClose(int hnd);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canBusOn")]
        public static extern canStatus canBusOn(int hnd);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canBusOff")]
        public static extern canStatus canBusOff(int hnd);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canSetBusParams")]
        public static extern canStatus canSetBusParams(int hnd, nint freq, int tseg1, int tseg2, int sjw, int noSamp, int syncmode);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canSetBusParamsFd")]
        public static extern canStatus canSetBusParamsFd(int hnd, nint freq_brs, int tseg1_brs, int tseg2_brs, int sjw_brs);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canGetNumberOfChannels")]
        public static extern canStatus canGetNumberOfChannels(out int channelCount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern canStatus canGetChannelData(int channel, int item, IntPtr buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canReadStatus")]
        public static extern canStatus canReadStatus(int hnd, out nuint flags);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canReadErrorCounters")]
        public static extern canStatus canReadErrorCounters(int hnd, out uint txErr, out uint rxErr, out uint ovErr);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static unsafe extern canStatus canWrite(int hnd, nint id, byte* msg, uint dlc, uint flag);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canRead")]
        public static extern canStatus canRead(int hnd, out nint id, [Out] byte[] msg, out int dlc, out int flag, out nuint time);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canRequestBusStatistics")]
        public static extern canStatus canRequestBusStatistics(int hnd);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canGetBusStatistics")]
        public static extern canStatus canGetBusStatistics(int hnd, out LinuxCanBusStatistics stat, UIntPtr bufsiz);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern canStatus canGetErrorText(canStatus err, StringBuilder buf, uint bufsiz);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern canStatus canIoCtl(int hnd, uint func, IntPtr buf, uint buflen);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canIoCtl")]
        public static extern canStatus canIoCtl_int(int hnd, uint func, ref int value, uint buflen);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canIoCtl")]
        public static extern canStatus canIoCtl_uint(int hnd, uint func, ref uint value, uint buflen);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canIoCtl")]
        public static extern canStatus canIoCtl_bytes(int hnd, uint func, byte[] buffer, uint buflen);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "canIoCtl")]
        public static extern canStatus canIoCtl_sb(int hnd, uint func, StringBuilder sb, uint buflen);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern canStatus canAccept(int hnd, nint envelope, uint flag);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canObjBufAllocate")]
        public static extern canStatus canObjBufAllocate(int hnd, int type);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canObjBufFree")]
        public static extern canStatus canObjBufFree(int hnd, int idx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canObjBufEnable")]
        public static extern canStatus canObjBufEnable(int hnd, int idx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canObjBufDisable")]
        public static extern canStatus canObjBufDisable(int hnd, int idx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canObjBufWrite")]
        public static extern canStatus canObjBufWrite(int hnd, int idx, int id, byte[] msg, uint dlc, uint flags);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canObjBufSetPeriod")]
        public static extern canStatus canObjBufSetPeriod(int hnd, int idx, uint periodUs);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_UInt32(int channel, int item, out uint value, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_UInt32Array(int channel, int item, uint[] buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_UInt64Array(int channel, int item, ulong[] buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_Ansi(int channel, int item, StringBuilder buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_Wide(int channel, int item, StringBuilder buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "canGetChannelData")]
        public static extern canStatus canGetChannelData_Bytes(int channel, int item, byte[] buffer, UIntPtr bufsize);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "kvSetNotifyCallback")]
        public static extern canStatus kvSetNotifyCallback(int hnd, kvCallbackDelegateCdecl? callback, IntPtr context, uint notifyFlags);

    }

    public static canStatus canSetAcceptanceFilter(int hnd, uint code, uint mask, int is_extended)
    {
        var re = canAccept(hnd, (int)code, (uint)(canFILTER_SET_CODE_STD + is_extended));
        if (re != canStatus.canOK)
            return re;
        re = canAccept(hnd, (int)code, (uint)(canFILTER_SET_MASK_STD + is_extended));
        return re;
    }

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
