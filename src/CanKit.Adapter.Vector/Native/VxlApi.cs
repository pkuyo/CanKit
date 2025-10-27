#if !FAKE
using System;
using System.Runtime.InteropServices;

namespace CanKit.Adapter.Vector.Native;

/// <summary>
/// P/Invoke surface for the Vector XL Driver (vxlapi.dll / vxlapi64.dll).
/// Only the members required by the adapter are exposed here.
/// </summary>
internal static class VxlApi
{
    private const string DllNameX64 = "vxlapi64";
    private const string DllNameX86 = "vxlapi";

    public const int RX_BATCH_COUNT = 256;
    public const int TX_BATCH_COUNT = 32;
    private static readonly bool Is64BitProcess = Environment.Is64BitProcess;


    public enum XLeventType : byte
    {
        XL_NO_COMMAND = 0,
        XL_RECEIVE_MSG = 1,
        XL_CHIP_STATE  = 4,
        XL_TRANSCEIVER = 6,
        XL_TIMER = 8,
        XL_TRANSMIT_MSG = 10,
        XL_SYNC_PULSE = 11,
        XL_APPLICATION_NOTIFICATION = 15,

    };

    public const int XL_SUCCESS = 0;
    public const int XL_ERR_QUEUE_IS_EMPTY = 10;
    public const int XL_ERR_INVALID_PORT = 118;
    public const int XL_INTERFACE_VERSION = 3;
    public const int XL_INTERFACE_VERSION_V4 = 4;
    public const int XL_ERR_TX_NOT_POSSIBLE = 12;
    public const int XL_ERR_INVALID_ACCESS = 112;
    public const int XL_ERR_HW_NOT_PRESENT = 129;

    public const ushort XL_CAN_EV_TAG_RX_OK = 0x0400;
    public const ushort XL_CAN_EV_TAG_RX_ERROR = 0x0401;
    public const ushort XL_CAN_EV_TAG_TX_ERROR = 0x0402;
    public const ushort XL_CAN_EV_TAG_TX_OK = 0x0404;
    public const ushort XL_CAN_EV_TAG_CHIP_STATE = 0x0409;

    public const ushort XL_CAN_EV_TAG_TX_MSG = 0x0440;
    public const ushort XL_CAN_EV_TAG_TX_ERRFR = 0x0441;

    public const byte XL_EVENT_TAG_TRANSMIT_MSG = 0x0A;

    public const uint XL_CAN_EXT_MSG_ID = 0x8000_0000u;

    public const uint XL_CAN_RXMSG_FLAG_EDL = 0x0001;
    public const uint XL_CAN_RXMSG_FLAG_BRS = 0x0002;
    public const uint XL_CAN_RXMSG_FLAG_ESI = 0x0004;
    public const uint XL_CAN_RXMSG_FLAG_RTR = 0x0010;
    public const uint XL_CAN_RXMSG_FLAG_EF = 0x0200;
    public const uint XL_CAN_RXMSG_FLAG_ARB_LOST = 0x0400;
    public const uint XL_CAN_RXMSG_FLAG_WAKEUP = 0x2000;
    public const uint XL_CAN_RXMSG_FLAG_TE = 0x4000;

    public const ushort XL_CAN_MSG_FLAG_REMOTE_FRAME = 0x0010;

    public const uint XL_CAN_TXMSG_FLAG_EDL = 0x0001;
    public const uint XL_CAN_TXMSG_FLAG_BRS = 0x0002;
    public const uint XL_CAN_TXMSG_FLAG_RTR = 0x0010;
    public const uint XL_CAN_TXMSG_FLAG_HIGHPRIO = 0x0080;
    public const uint XL_CAN_TXMSG_FLAG_WAKEUP = 0x0200;

    public const byte XL_OUTPUT_MODE_SILENT = 0;
    public const byte XL_OUTPUT_MODE_NORMAL = 1;
    public const byte XL_OUTPUT_MODE_TX_OFF = 2;
    public const byte XL_OUTPUT_MODE_SJA_1000_SILENT = 3;

    public const uint XL_BUS_TYPE_CAN = 0x0000_0001;
    public const uint XL_BUS_COMPATIBLE_CAN = 0x0000_0001;
    public const uint XL_BUS_ACTIVE_CAP_CAN = 0x0001_0000;
    public const uint XL_CHANNEL_FLAG_CANFD_BOSCH_SUPPORT = 0x2000_0000;
    public const uint XL_CHANNEL_FLAG_CANFD_ISO_SUPPORT = 0x8000_0000;

    public const ushort XL_CHIPSTAT_BUSOFF = 0x0001;
    public const ushort XL_CHIPSTAT_ERROR_PASSIVE = 0x0002;
    public const ushort XL_CHIPSTAT_ERROR_WARNING = 0x0004;
    public const ushort XL_CHIPSTAT_ERROR_ACTIVE = 0x0008;

    public const uint XL_CANFD_CONFOPT_NO_ISO = 0x0000_0008;

    public static ulong ChannelIndexToMask(int index) => 1UL << index;

    public static int SizeOfCanRxEvent => Marshal.SizeOf<XLcanRxEvent>();
    public static int SizeOfCanTxEvent => Marshal.SizeOf<XLcanTxEvent>();

    #region Structs

    [StructLayout(LayoutKind.Sequential)]
    public struct XLchipParams
    {
        public uint BitRate;
        public byte Sjw;
        public byte Tseg1;
        public byte Tseg2;
        public byte Sam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XLcanFdConf
    {
        public uint ArbitrationBitRate;
        public uint SjwAbr;
        public uint Tseg1Abr;
        public uint Tseg2Abr;
        public uint DataBitRate;
        public uint SjwDbr;
        public uint Tseg1Dbr;
        public uint Tseg2Dbr;
        public byte Reserved;    // must be zero
        public byte Options;     // CANFD_CONFOPT_
        public ushort Reserved1; // must be zero
        public uint Reserved2;   // must be zero
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XLcanTxMsg
    {
        public uint CanId;
        public uint MsgFlags;
        public byte Dlc;
        public fixed byte Reserved[7];
        public fixed byte Data[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XLcanTxTagData
    {
        public XLcanTxMsg CanMsg;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XLcanTxEvent
    {
        public ushort Tag;
        public ushort TransId;
        public byte ChanIndex;
        public fixed byte Reserved[3];
        public XLcanTxTagData TagData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XLcanRxMsg
    {
        public uint CanId;
        public uint MsgFlags;
        public uint Crc;
        public fixed byte Reserved1[12];
        public ushort TotalBitCnt;
        public byte Dlc;
        public fixed byte Reserved2[5];
        public fixed byte Data[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XLcanTxRequest
    {
        public uint CanId;
        public uint MsgFlags;
        public byte Dlc;
        public byte TxAttemptConf;
        public ushort Reserved;
        public fixed byte Data[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XLcanRxError
    {
        public byte ErrorCode;
        public fixed byte Reserved[95];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XLchipState
    {
        public byte BusStatus;
        public byte TxErrorCounter;
        public byte RxErrorCounter;
        public byte Reserved;
        public uint Reserved0;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct XLchipStateBasic
    {
        public byte BusStatus;
        public byte TxErrorCounter;
        public byte RxErrorCounter;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct XLcanRxTagData
    {
        [FieldOffset(0)]
        public XLcanRxMsg CanRxOkMsg;

        [FieldOffset(0)]
        public XLcanRxMsg CanTxOkMsg;

        [FieldOffset(0)]
        public XLcanTxRequest CanTxRequest;

        [FieldOffset(0)]
        public XLcanRxError CanError;

        [FieldOffset(0)]
        public XLchipState ChipState;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XLcanRxEvent
    {
        public int Size;
        public ushort Tag;
        public byte ChanIndex;
        public byte Reserved;
        public int UserHandle;
        public ushort FlagsChip;
        public ushort Reserved0;
        public ulong Reserved1;
        public ulong TimeStamp;
        public XLcanRxTagData TagData;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct XLcanMsg
    {
        public uint Id;
        public ushort Flags;
        public ushort Dlc;
        public ulong Res1;
        public fixed byte Data[8];
        public ulong Res2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct XLsyncPulse
    {
        public byte PulseCode;
        public ulong Time;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct XLeventTagData
    {
        [FieldOffset(0)]
        public XLcanMsg Msg;

        [FieldOffset(0)]
        public XLchipStateBasic ChipState;

        [FieldOffset(0)]
        public XLsyncPulse SyncPulse;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct XLevent
    {
        public byte Tag;
        public byte ChanIndex;
        public ushort TransId;
        public ushort PortHandle;
        public byte Flags;
        public byte Reserved;
        public ulong TimeStamp;
        public XLeventTagData TagData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XLbusParams
    {
        public uint BusType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 28)]
        public byte[] Data;
    }

    // XL_CHANNEL_CONFIG is defined under pack(8) in vxlapi.h
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct XLchannelConfig
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;

        public byte HwType;
        public byte HwIndex;
        public byte HwChannel;
        public ushort TransceiverType;
        public ushort TransceiverState;
        public ushort ConfigError;
        public byte ChannelIndex;
        public ulong ChannelMask;
        public uint ChannelCapabilities;
        public uint ChannelBusCapabilities;
        public byte IsOnBus;
        public uint ConnectedBusType;
        public XLbusParams BusParams;
        public uint DoNotUse;
        public uint DriverVersion;
        public uint InterfaceVersion;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public uint[] RawData;

        public uint SerialNumber;
        public uint ArticleNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string TransceiverName;

        public uint SpecialCabFlags;
        public uint DominantTimeout;
        public byte DominantRecessiveDelay;
        public byte RecessiveDominantDelay;
        public byte ConnectionInfo;
        public byte CurrentlyAvailableTimestamps;
        public ushort MinimalSupplyVoltage;
        public ushort MaximalSupplyVoltage;
        public uint MaximalBaudrate;
        public byte FpgaCoreCapabilities;
        public byte SpecialDeviceStatus;
        public ushort ChannelBusActiveCapabilities;
        public ushort BreakOffset;
        public ushort DelimiterOffset;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct XLdriverConfig
    {
        public uint DllVersion;
        public uint ChannelCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public uint[] Reserved;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public XLchannelConfig[] Channel;
    }

    #endregion

    #region Native helpers

    public static string GetErrorString(int status)
    {
        var ptr = Is64BitProcess ? Native64.xlGetErrorString(status) : Native32.xlGetErrorString(status);
        return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? string.Empty : string.Empty;
    }

    public static int xlOpenDriver() => Is64BitProcess ? Native64.xlOpenDriver() : Native32.xlOpenDriver();

    public static int xlCloseDriver() => Is64BitProcess ? Native64.xlCloseDriver() : Native32.xlCloseDriver();

    public static int xlGetDriverConfig(ref XLdriverConfig config)
        => Is64BitProcess ? Native64.xlGetDriverConfig(ref config) : Native32.xlGetDriverConfig(ref config);

    public static int xlOpenPort(ref int portHandle, string appName, ulong accessMask, ref ulong permissionMask, uint rxQueueSize, uint xlInterfaceVersion, uint busType)
        => Is64BitProcess
            ? Native64.xlOpenPort(ref portHandle, appName, accessMask, ref permissionMask, rxQueueSize, xlInterfaceVersion, busType)
            : Native32.xlOpenPort(ref portHandle, appName, accessMask, ref permissionMask, rxQueueSize, xlInterfaceVersion, busType);

    public static int xlClosePort(int portHandle)
        => Is64BitProcess ? Native64.xlClosePort(portHandle) : Native32.xlClosePort(portHandle);

    public static int xlActivateChannel(int portHandle, ulong accessMask, uint busType, uint flags)
        => Is64BitProcess ? Native64.xlActivateChannel(portHandle, accessMask, busType, flags)
                          : Native32.xlActivateChannel(portHandle, accessMask, busType, flags);

    public static int xlDeactivateChannel(int portHandle, ulong accessMask)
        => Is64BitProcess ? Native64.xlDeactivateChannel(portHandle, accessMask) : Native32.xlDeactivateChannel(portHandle, accessMask);

    public static int xlCanSetChannelBitrate(int portHandle, ulong accessMask, uint bitrate)
        => Is64BitProcess ? Native64.xlCanSetChannelBitrate(portHandle, accessMask, bitrate)
                          : Native32.xlCanSetChannelBitrate(portHandle, accessMask, bitrate);

    public static int xlCanSetChannelParams(int portHandle, ulong accessMask, ref XLchipParams parameters)
        => Is64BitProcess ? Native64.xlCanSetChannelParams(portHandle, accessMask, ref parameters)
                          : Native32.xlCanSetChannelParams(portHandle, accessMask, ref parameters);

    public static int xlCanFdSetConfiguration(int portHandle, ulong accessMask, ref XLcanFdConf conf)
        => Is64BitProcess ? Native64.xlCanFdSetConfiguration(portHandle, accessMask, ref conf)
                          : Native32.xlCanFdSetConfiguration(portHandle, accessMask, ref conf);

    public static int xlCanSetChannelOutput(int portHandle, ulong accessMask, int outputMode)
        => Is64BitProcess ? Native64.xlCanSetChannelOutput(portHandle, accessMask, outputMode)
                          : Native32.xlCanSetChannelOutput(portHandle, accessMask, outputMode);

    public static int xlCanSetChannelAcceptance(int portHandle, ulong accessMask, uint code, uint mask, uint acceptanceType)
        => Is64BitProcess
            ? Native64.xlCanSetChannelAcceptance(portHandle, accessMask, code, mask, acceptanceType)
            : Native32.xlCanSetChannelAcceptance(portHandle, accessMask, code, mask, acceptanceType);

    public static int xlCanResetAcceptance(int portHandle, ulong accessMask, uint acceptanceType)
        => Is64BitProcess
            ? Native64.xlCanResetAcceptance(portHandle, accessMask, acceptanceType)
            : Native32.xlCanResetAcceptance(portHandle, accessMask, acceptanceType);

    public static int xlCanAddAcceptanceRange(int portHandle, ulong accessMask, uint from, uint to)
        => Is64BitProcess
            ? Native64.xlCanAddAcceptanceRange(portHandle, accessMask, from, to)
            : Native32.xlCanAddAcceptanceRange(portHandle, accessMask, from, to);

    public static int xlFlushReceiveQueue(int portHandle)
        => Is64BitProcess ? Native64.xlFlushReceiveQueue(portHandle) : Native32.xlFlushReceiveQueue(portHandle);

    public static int xlCanReceive(int portHandle, out XLcanRxEvent ev)
        => Is64BitProcess ? Native64.xlCanReceive(portHandle, out ev) : Native32.xlCanReceive(portHandle, out ev);

    public static int xlSetNotification(int portHandle, out IntPtr eventHandle, int queueLevel)
        => Is64BitProcess ? Native64.xlSetNotification(portHandle, out eventHandle, queueLevel) : Native32.xlSetNotification(portHandle, out eventHandle, queueLevel);

    public static unsafe int xlReceive(int portHandle, ref int eventCount, XLevent* ev)
        => Is64BitProcess ? Native64.xlReceive(portHandle, ref eventCount, ev) :
            Native32.xlReceive(portHandle, ref eventCount, ev);
    public static unsafe int xlCanTransmit(int portHandle, ulong accessMask, ref uint count, XLevent* events)
        => Is64BitProcess
            ? Native64.xlCanTransmit(portHandle, accessMask, ref count, events)
            : Native32.xlCanTransmit(portHandle, accessMask, ref count, events);

    public static unsafe int xlCanTransmitEx(int portHandle, ulong accessMask, uint messageCount, ref uint sentCount, XLcanTxEvent* events)
        => Is64BitProcess
            ? Native64.xlCanTransmitEx(portHandle, accessMask, messageCount, ref sentCount, events)
            : Native32.xlCanTransmitEx(portHandle, accessMask, messageCount, ref sentCount, events);

    public static int xlGetApplConfig(string appName, uint appChannel, ref uint hwType, ref uint hwIndex, ref uint hwChannel, uint busType)
        => Is64BitProcess
            ? Native64.xlGetApplConfig(appName, appChannel, ref hwType, ref hwIndex, ref hwChannel, busType)
            : Native32.xlGetApplConfig(appName, appChannel, ref hwType, ref hwIndex, ref hwChannel, busType);

    public static int xlGetChannelIndex(int hwType, int hwIndex, int hwChannel)
        => Is64BitProcess
            ? Native64.xlGetChannelIndex(hwType, hwIndex, hwChannel)
            : Native32.xlGetChannelIndex(hwType, hwIndex, hwChannel);

    public static int xlCanRequestChipState(int portHandle, ulong accessMask)
        => Is64BitProcess
            ? Native64.xlCanRequestChipState(portHandle, accessMask)
            : Native32.xlCanRequestChipState(portHandle, accessMask);
    #endregion

    #region Native interop (x64/x86)

    private static class Native64
    {
        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlOpenDriver();

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCloseDriver();

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int xlGetDriverConfig(ref XLdriverConfig config);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr xlGetErrorString(int status);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int xlOpenPort(ref int portHandle, string appName, ulong accessMask, ref ulong permissionMask, uint rxQueueSize, uint xlInterfaceVersion, uint busType);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlClosePort(int portHandle);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlActivateChannel(int portHandle, ulong accessMask, uint busType, uint flags);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlDeactivateChannel(int portHandle, ulong accessMask);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanSetChannelBitrate(int portHandle, ulong accessMask, uint bitrate);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanSetChannelParams(int portHandle, ulong accessMask, ref XLchipParams parameters);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanFdSetConfiguration(int portHandle, ulong accessMask, ref XLcanFdConf conf);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanSetChannelOutput(int portHandle, ulong accessMask, int outputMode);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanSetChannelAcceptance(int portHandle, ulong accessMask, uint code, uint mask, uint acceptanceType);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanResetAcceptance(int portHandle, ulong accessMask, uint acceptanceType);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlCanAddAcceptanceRange(int portHandle, ulong accessMask, uint from, uint to);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlFlushReceiveQueue(int portHandle);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanReceive(int portHandle, out XLcanRxEvent ev);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlSetNotification(int portHandle, out IntPtr eventHandle, int queueLevel);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static unsafe extern int xlReceive(int portHandle, ref int eventCount, XLevent* ev);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static unsafe extern int xlCanTransmit(int portHandle, ulong accessMask, ref uint count, XLevent* events);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static unsafe extern int xlCanTransmitEx(int portHandle, ulong accessMask, uint messageCount, ref uint countSent, XLcanTxEvent* events);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int xlGetApplConfig(string appName, uint appChannel, ref uint hwType, ref uint hwIndex, ref uint hwChannel, uint busType);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlGetChannelIndex(int hwType, int hwIndex, int hwChannel);

        [DllImport(DllNameX64, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlCanRequestChipState(int portHandle, ulong accessMask);
    }

    private static class Native32
    {
        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlOpenDriver();

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCloseDriver();

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int xlGetDriverConfig(ref XLdriverConfig config);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr xlGetErrorString(int status);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int xlOpenPort(ref int portHandle, string appName, ulong accessMask, ref ulong permissionMask, uint rxQueueSize, uint xlInterfaceVersion, uint busType);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlClosePort(int portHandle);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlActivateChannel(int portHandle, ulong accessMask, uint busType, uint flags);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlDeactivateChannel(int portHandle, ulong accessMask);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanSetChannelBitrate(int portHandle, ulong accessMask, uint bitrate);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanSetChannelParams(int portHandle, ulong accessMask, ref XLchipParams parameters);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanFdSetConfiguration(int portHandle, ulong accessMask, ref XLcanFdConf conf);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanSetChannelOutput(int portHandle, ulong accessMask, int outputMode);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanSetChannelAcceptance(int portHandle, ulong accessMask, uint code, uint mask, uint acceptanceType);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanResetAcceptance(int portHandle, ulong accessMask, uint acceptanceType);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlCanAddAcceptanceRange(int portHandle, ulong accessMask, uint from, uint to);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlFlushReceiveQueue(int portHandle);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlCanReceive(int portHandle, out XLcanRxEvent ev);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlSetNotification(int portHandle, out IntPtr eventHandle, int queueLevel);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static unsafe extern int xlReceive(int portHandle, ref int eventCount, XLevent* ev);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static unsafe extern int xlCanTransmit(int portHandle, ulong accessMask, ref uint count, XLevent* events);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static unsafe extern int xlCanTransmitEx(int portHandle, ulong accessMask, uint messageCount, ref uint countSent, XLcanTxEvent* events);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int xlGetApplConfig(string appName, uint appChannel, ref uint hwType, ref uint hwIndex, ref uint hwChannel, uint busType);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xlGetChannelIndex(int hwType, int hwIndex, int hwChannel);

        [DllImport(DllNameX86, CallingConvention = CallingConvention.Cdecl)]
        public static extern int xlCanRequestChipState(int portHandle, ulong accessMask);
    }

    #endregion
}
#endif
