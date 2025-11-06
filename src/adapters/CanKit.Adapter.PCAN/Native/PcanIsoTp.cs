using System.Runtime.InteropServices;
using System.Text;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN.Native;

public static class PcanIsoTp
{
    private const string DllName = "PCAN-ISO-TP.dll";

    public const int MaxLengthHardwareName = 33;
    public const int MaxLengthVersionString = 256;


    public const byte PCanTpStminIso15765_2 = 10;
    public const byte PCanTpBsIso15765_2 = 10;
    public const uint PCanTpTimeoutArIso15765_2 = 1000u * 1000u;
    public const uint PCanTpTimeoutAsIso15765_2 = 1000u * 1000u;
    public const uint PCanTpTimeoutBrIso15765_2 = 1000u * 1000u;
    public const uint PCanTpTimeoutBsIso15765_2 = 1000u * 1000u;
    public const uint PCanTpTimeoutCrIso15765_2 = 1000u * 1000u;
    public const uint PCanTpTimeoutCsIso15765_2 = 1000u * 1000u;
    public const byte PCanTpTimeoutToleranceDefault = 0;

    public const byte PCanTpStminIso15765_4 = 0;
    public const byte PCanTpBsIso15765_4 = 0;
    public const uint PCanTpTimeoutArIso15765_4 = 1000u * 33u;
    public const uint PCanTpTimeoutAsIso15765_4 = 1000u * 33u;
    public const uint PCanTpTimeoutBrIso15765_4 = 1000u * 75u;
    public const uint PCanTpTimeoutBsIso15765_4 = 1000u * 75u;
    public const uint PCanTpTimeoutCrIso15765_4 = 1000u * 150u;
    public const uint PCanTpTimeoutCsIso15765_4 = 1000u * 17u;

    public const int MaxLengthCanStandard = 0x08;
    public const int MaxLengthCanFd = 0x40;
    public const uint MaxLengthIsoTp2004 = 0x0FFFu;
    public const uint MaxLengthIsoTp2016 = 0xFFFFFFFFu;
    public const uint MaxLengthAllocBeforeVirtual = 0x00FFFFFFu;
    public const byte DefaultJ1939Priority = 0x06;
    public const uint MaxCanId11Bit = 0x7FFu;
    public const uint MaxCanId29Bit = 0x1FFFFFFFu;
    public const byte DefaultCanTxDlc = MaxLengthCanStandard;
    public const byte DefaultCanPaddingValue = 0x55;

    public const string LookupDeviceType = "devicetype";
    public const string LookupDeviceId = "deviceid";
    public const string LookupControllerNumber = "controllernumber";
    public const string LookupIpAddress = "ipaddress";

    public const string BrKeyFClock = "f_clock";
    public const string BrKeyFClockMhz = "f_clock_mhz";
    public const string BrKeyNomBrp = "nom_brp";
    public const string BrKeyNomTseg1 = "nom_tseg1";
    public const string BrKeyNomTseg2 = "nom_tseg2";
    public const string BrKeyNomSjw = "nom_sjw";
    public const string BrKeyNomSample = "nom_sam";
    public const string BrKeyDataBrp = "data_brp";
    public const string BrKeyDataTseg1 = "data_tseg1";
    public const string BrKeyDataTseg2 = "data_tseg2";
    public const string BrKeyDataSjw = "data_sjw";
    public const string BrKeyDataSample = "data_ssp_offset";

    public enum PCanTpDebugParameter : uint
    {
        None = 0,
        Can = 1,
        Notice = 0xF4,
        Info = 0xF3,
        Warning = 0xF2,
        Error = 0xF1,
    }

    [Flags]
    public enum PCanTpInfoStatus : uint
    {
        Ok = 0x00,
        CautionInputModified = 0x01,
        CautionDlcModified = 0x02,
        CautionDataLengthModified = 0x04,
        CautionFdFlagModified = 0x08,
        CautionRxQueueFull = 0x10,
        CautionBufferInUse = 0x20,
        CautionRxQueueOverrun = 0x30
    }

    [Flags]
    public enum PCanTpStatus : uint
    {
        Ok = 0x00000000,
        NotInitialized = 0x00000001,
        AlreadyInitialized = 0x00000002,
        NoMemory = 0x00000003,
        Overflow = 0x00000004,
        NoMessage = 0x00000007,
        ParamInvalidType = 0x00000008,
        ParamInvalidValue = 0x00000009,
        MappingNotInitialized = 0x0000000D,
        MappingInvalid = 0x0000000E,
        MappingAlreadyInitialized = 0x0000000F,
        ParamBufferTooSmall = 0x00000010,
        QueueTxFull = 0x00000011,
        LockTimeout = 0x00000012,
        HandleInvalid = 0x00000013,
        Unknown = 0x000000FF,

        BusLight = 0x00000100,
        BusHeavy = 0x00000200,
        BusWarning = 0x00000200,
        BusPassive = 0x00000400,
        BusOff = 0x00000800,
        BusAny = 0x00000F00,

        NetworkResult = 0x00002000,
        NetworkTimeoutA = 0x00006000,
        NetworkTimeoutBs = 0x0000A000,
        NetworkTimeoutCr = 0x0000E000,
        NetworkWrongSn = 0x00012000,
        NetworkInvalidFs = 0x00016000,
        NetworkUnexpPdu = 0x0001A000,
        NetworkWftOvrn = 0x0001E000,
        NetworkBufferOvflw = 0x00022000,
        NetworkError = 0x00026000,
        NetworkIgnored = 0x0002A000,
        NetworkTimeoutAs = 0x0002E000,
        NetworkTimeoutAr = 0x00032000,

        CautionInputModified = 0x00040000,
        CautionDlcModified = 0x00080000,
        CautionDataLengthModified = 0x00100000,
        CautionFdFlagModified = 0x00200000,
        CautionRxQueueFull = 0x00400000,
        CautionBufferInUse = 0x00800000,
        CautionRxQueueOverrun = 0x00C00000,

        PcanStatusFlag = 0x80000000
    }

    public static class PCanTpStatusMasks
    {
        public const uint Error = 0x000000FF;
        public const uint Bus = 0x00001F00;
        public const uint IsoTpNet = 0x0003E000;
        public const uint Info = 0x00FC0000;
        public const uint Pcan = 0x7FFFFFFF;
    }

    public enum PCanTpStatusType : uint
    {
        Ok = 0x00,
        Err = 0x01,
        Bus = 0x02,
        Net = 0x04,
        Info = 0x08,
        Pcan = 0x10
    }

    public enum PCanTpNetStatus : uint
    {
        Ok = 0x00,
        TimeoutA = 0x01,
        TimeoutBs = 0x02,
        TimeoutCr = 0x03,
        WrongSn = 0x04,
        InvalidFs = 0x05,
        UnexpPdu = 0x06,
        WftOvrn = 0x07,
        BufferOvflw = 0x08,
        Error = 0x09,
        Ignored = 0x0A,
        TimeoutAs = 0x0B,
        TimeoutAr = 0x0C,
        XmtFull = 0x0D,
        BusError = 0x0E,
        NoMemory = 0x0F
    }

    [Flags]
    public enum PCanTpMsgType : uint
    {
        None = 0x00,
        Can = 0x01,
        CanFd = 0x02,
        IsoTp = 0x04,
        CanInfo = 0x08,
        Frame = 0x03,
        Any = 0xFFFFFFFF
    }

    [Flags]
    public enum PCanTpMsgFlag : uint
    {
        None = 0,
        Loopback = 1,
        IsotpFrame = 2,
        QueueOverrunOccurred = 4
    }

    public enum PCanTpIsotpMsgType : uint
    {
        Unknown = 0x00,
        Diagnostic = 0x01,
        RemoteDiagnostic = 0x02,
        FlagIndicationRx = 0x10,
        FlagIndicationTx = 0x20,
        FlagIndication = 0x30,
        MaskIndication = 0x0F
    }

    public enum PCanTpIsotpFormat : uint
    {
        Unknown = 0xFF,
        None = 0x00,
        Normal = 0x01,
        FixedNormal = 0x02,
        Extended = 0x03,
        Mixed = 0x04,
        Enhanced = 0x05
    }

    public enum PCanTpIsotpAddressing : uint
    {
        Unknown = 0x00,
        Physical = 0x01,
        Functional = 0x02
    }

    public enum PCanTpOption : uint
    {
        Debug = 0x103,
        CanDataPadding = 0x107,
        CanPaddingValue = 0x108,
        J1939Priority = 0x10A,
        MsgPending = 0x10B,
        BlockSize = 0x10C,
        BlockSizeTx = 0x10D,
        SeparationTime = 0x10E,
        SeparationTimeTx = 0x10F,
        WftMax = 0x123,
        WftMaxTx = 0x110,
        TimeoutAs = 0x111,
        TimeoutAr = 0x112,
        TimeoutBs = 0x113,
        TimeoutBr = 0x121,
        TimeoutCs = 0x122,
        TimeoutCr = 0x114,
        SelfReceiveLatency = 0x117
    }

    public enum PCanTpMsgProgressState : uint
    {
        Queued = 0,
        Processing = 1,
        Completed = 2,
        Unknown = 3
    }

    public enum PCanTpMsgDirection : uint
    {
        Rx = 0,
        Tx = 1
    }

    public enum PCanTpParameter : uint
    {
        ApiVersion = 0x101,
        ChannelCondition = 0x102,
        Debug = 0x103,
        ReceiveEvent = 0x104,
        FrameFiltering = 0x105,
        CanTxDlc = 0x106,
        CanDataPadding = 0x107,
        CanPaddingValue = 0x108,
        IsoRevision = 0x109,
        J1939Priority = 0x10A,
        MsgPending = 0x10B,
        BlockSize = 0x10C,
        BlockSizeTx = 0x10D,
        SeparationTime = 0x10E,
        SeparationTimeTx = 0x10F,
        WftMaxTx = 0x110,
        TimeoutAs = 0x111,
        TimeoutAr = 0x112,
        TimeoutBs = 0x113,
        TimeoutCr = 0x114,
        TimeoutTolerance = 0x115,
        IsoTimeouts = 0x116,
        SelfReceiveLatency = 0x117,
        MaxRxQueue = 0x118,
        KeepHigherLayerMessages = 0x119,
        FilterCanId = 0x11A,
        Support29bEnhanced = 0x11B,
        Support29bFixedNormal = 0x11C,
        Support29bMixed = 0x11D,
        MsgCheck = 0x11E,
        ResetHard = 0x11F,
        NetworkLayerDesign = 0x120,
        TimeoutBr = 0x121,
        TimeoutCs = 0x122,
        WftMax = 0x123,
        AllowMsgtypeCanInfo = 0x124,
        ReceiveEventCallback = 0x125,
        ReceiveEventCallbackUserContext = 0x126,
        HardwareName = 0x80, // PCAN_BASIC passthrough expected
        DeviceId = 0x81, // PCAN_BASIC passthrough expected
        ControllerNumber = 0x83, // PCAN_BASIC passthrough expected
        ChannelFeatures = 0x84 // PCAN_BASIC passthrough expected
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PCanTpMsgInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PCanTpMsgOption
    {
        public PCanTpOption Name;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PCanTpMsgOptionList
    {
        public IntPtr Buffer;
        public uint Count;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PCanTpCanInfo
    {
        public uint CanId;
        public MessageType CanMsgType;
        public byte Dlc;
        public byte _pad1;
        public ushort _pad2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PCanTpNetAddrInfo
    {
        public PCanTpIsotpMsgType MsgType;
        public PCanTpIsotpFormat Format;
        public PCanTpIsotpAddressing TargetType;
        public ushort SourceAddr;
        public ushort TargetAddr;
        public byte ExtensionAddr;
        public byte _pad1;
        public ushort _pad2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PCanTpMapping
    {
        public UIntPtr Uid;
        public uint CanId;
        public uint CanIdFlowCtrl;
        public MessageType CanMsgType;
        public byte CanTxDlc;
        public byte _pad1;
        public ushort _pad2;
        public PCanTpNetAddrInfo NetAddrInfo;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PCanTpMsgData
    {
        [MarshalAs(UnmanagedType.U4)]
        public PCanTpMsgFlag Flags;
        public uint Length;
        public IntPtr Data;
        [MarshalAs(UnmanagedType.U4)]
        public PCanTpNetStatus NetStatus;
        public IntPtr Options;
    }

    public unsafe struct PCanTpMsgDataCan
    {
        [MarshalAs(UnmanagedType.U4)]
        public PCanTpMsgFlag Flags;
        public uint Length;
        public byte* Data;
        [MarshalAs(UnmanagedType.U4)]
        public PCanTpNetStatus NetStatus;
        public IntPtr Options;
        public fixed byte DataMax[MaxLengthCanStandard];
    }

    public unsafe struct PCanTpMsgDataCanFd
    {
        [MarshalAs(UnmanagedType.U4)]
        public PCanTpMsgFlag Flags;
        public uint Length;
        public byte* Data;
        [MarshalAs(UnmanagedType.U4)]
        public PCanTpNetStatus NetStatus;
        public IntPtr Options;
        public fixed byte DataMax[MaxLengthCanFd];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct PCanTpMsgDataIsoTp
    {
        [MarshalAs(UnmanagedType.U4)]
        public PCanTpMsgFlag Flags;
        public uint Length;
        public byte* Data;
        [MarshalAs(UnmanagedType.U4)]
        public PCanTpNetStatus NetStatus;
        public IntPtr Options;
        public PCanTpNetAddrInfo NetAddrInfo;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    public struct PCanTpMsgDataUnion
    {
        [FieldOffset(0)] public PCanTpMsgDataCan Can;
        [FieldOffset(0)] public PCanTpMsgDataCanFd CanFd;
        [FieldOffset(0)] public PCanTpMsgDataIsoTp IsoTp;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PCanTpMsg
    {
        public PCanTpMsgType Type;
        public PCanTpMsgInfo Reserved;
        public PCanTpCanInfo CanInfo;
        public PCanTpMsgDataUnion MsgData;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PCanTpMsgProgress
    {
        public PCanTpMsgProgressState State;
        public byte Percentage;
        public byte _pad1;
        public ushort _pad2;
        public IntPtr Buffer;
    }


    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PCanTpMsgrule
    {
        public UIntPtr Uid;
        public MessageType Type;
        public PCanTpCanInfo CanInfo;
        public PCanTpNetAddrInfo NetAddrInfo;
        public PCanTpMsgOptionList Options;
        public UIntPtr Reserved;
    }

    [DllImport(DllName, EntryPoint = "CANTP_Initialize_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true, CharSet = CharSet.Ansi)]
    public static extern PCanTpStatus Initialize(PcanChannel channel, Bitrate baudrate, uint hwType = 0, uint ioPort = 0, ushort interrupt = 0);

    [DllImport(DllName, EntryPoint = "CANTP_InitializeFD_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true, CharSet = CharSet.Ansi)]
    public static extern PCanTpStatus InitializeFd(PcanChannel channel, string bitrateFd);

    [DllImport(DllName, EntryPoint = "CANTP_Uninitialize_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus Uninitialize(PcanChannel channel);

    [DllImport(DllName, EntryPoint = "CANTP_Reset_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus Reset(PcanChannel channel);

    [DllImport(DllName, EntryPoint = "CANTP_GetCanBusStatus_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus GetCanBusStatus(PcanChannel channel);


    [DllImport(DllName, EntryPoint = "CANTP_Read_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static unsafe extern PCanTpStatus Read(PcanChannel channel, PCanTpMsg* msgBuffer, ulong* timestamp, PCanTpMsgType msgType);

    [DllImport(DllName, EntryPoint = "CANTP_Write_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static unsafe extern PCanTpStatus Write(PcanChannel channel, PCanTpMsg* msgBuffer);

    [DllImport(DllName, EntryPoint = "CANTP_GetMsgProgress_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static unsafe extern PCanTpStatus GetMsgProgress(PcanChannel channel, PCanTpMsg* msgBuffer, PCanTpMsgDirection direction, PCanTpMsgProgress* progressBuffer);


    [DllImport(DllName, EntryPoint = "CANTP_GetValue_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus GetValue(PcanChannel channel, PCanTpParameter parameter, ref byte buffer, uint bufferSize);

    [DllImport(DllName, EntryPoint = "CANTP_GetValue_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus GetValue(PcanChannel channel, PCanTpParameter parameter, ref ushort buffer, uint bufferSize);

    [DllImport(DllName, EntryPoint = "CANTP_GetValue_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus GetValue(PcanChannel channel, PCanTpParameter parameter, ref uint buffer, uint bufferSize);

    [DllImport(DllName, EntryPoint = "CANTP_GetValue_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus GetValue(PcanChannel channel, PCanTpParameter parameter, ref ulong buffer, uint bufferSize);

    [DllImport(DllName, EntryPoint = "CANTP_GetValue_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus GetValue(PcanChannel channel, PCanTpParameter parameter, IntPtr buffer, uint bufferSize);

    [DllImport(DllName, EntryPoint = "CANTP_SetValue_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus SetValue(PcanChannel channel, PCanTpParameter parameter, ref byte buffer, uint bufferSize);

    [DllImport(DllName, EntryPoint = "CANTP_SetValue_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus SetValue(PcanChannel channel, PCanTpParameter parameter, ref ushort buffer, uint bufferSize);

    [DllImport(DllName, EntryPoint = "CANTP_SetValue_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus SetValue(PcanChannel channel, PCanTpParameter parameter, ref uint buffer, uint bufferSize);

    [DllImport(DllName, EntryPoint = "CANTP_SetValue_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus SetValue(PcanChannel channel, PCanTpParameter parameter, ref ulong buffer, uint bufferSize);

    [DllImport(DllName, EntryPoint = "CANTP_SetValue_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus SetValue(PcanChannel channel, PCanTpParameter parameter, IntPtr buffer, uint bufferSize);

    [DllImport(DllName, EntryPoint = "CANTP_AddMapping_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus AddMapping(PcanChannel channel, ref PCanTpMapping mapping);

    [DllImport(DllName, EntryPoint = "CANTP_RemoveMappings_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus RemoveMappings(PcanChannel channel, uint canId);

    [DllImport(DllName, EntryPoint = "CANTP_RemoveMapping_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus RemoveMapping(PcanChannel channel, UIntPtr uid);

    [DllImport(DllName, EntryPoint = "CANTP_GetMappings_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus GetMappings(PcanChannel channel, IntPtr buffer, ref uint bufferLength);

    [DllImport(DllName, EntryPoint = "CANTP_AddFiltering_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus AddFiltering(PcanChannel channel, uint canIdFrom, uint canIdTo, [MarshalAs(UnmanagedType.I1)] bool ignoreCanMsgType, MessageType canMsgType);

    [DllImport(DllName, EntryPoint = "CANTP_RemoveFiltering_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus RemoveFiltering(PcanChannel channel, uint canIdFrom, uint canIdTo, [MarshalAs(UnmanagedType.I1)] bool ignoreCanMsgType, MessageType canMsgType);

    [DllImport(DllName, EntryPoint = "CANTP_AddMsgRule_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus AddMsgRule(PcanChannel channel, ref PCanTpMsgrule msgRule);

    [DllImport(DllName, EntryPoint = "CANTP_RemoveMsgRule_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus RemoveMsgRule(PcanChannel channel, UIntPtr uid);

    [DllImport(DllName, EntryPoint = "CANTP_GetErrorText_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true, CharSet = CharSet.Ansi)]
    public static extern PCanTpStatus GetErrorText(PCanTpStatus error, ushort language, StringBuilder buffer, uint bufferSize);

    [DllImport(DllName, EntryPoint = "CANTP_MsgDataAlloc_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus MsgDataAlloc(ref PCanTpMsg msg, PCanTpMsgType type);

    [DllImport(DllName, EntryPoint = "CANTP_MsgDataInit_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus MsgDataInit(ref PCanTpMsg msg, uint canId, MessageType canMsgType, uint dataLength, IntPtr data, IntPtr netAddrInfo);

    [DllImport(DllName, EntryPoint = "CANTP_MsgDataInitOptions_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus MsgDataInitOptions(ref PCanTpMsg msg, uint nbOptions);

    [DllImport(DllName, EntryPoint = "CANTP_MsgDataFree_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus MsgDataFree(ref PCanTpMsg msg);

    [DllImport(DllName, EntryPoint = "CANTP_MsgEqual_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool MsgEqual(ref PCanTpMsg a, ref PCanTpMsg b, [MarshalAs(UnmanagedType.I1)] bool ignoreSelfReceiveFlag);

    [DllImport(DllName, EntryPoint = "CANTP_MsgCopy_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatus MsgCopy(ref PCanTpMsg dst, ref PCanTpMsg src);

    [DllImport(DllName, EntryPoint = "CANTP_MsgDlcToLength_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern uint MsgDlcToLength(byte dlc);

    [DllImport(DllName, EntryPoint = "CANTP_MsgLengthToDlc_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern byte MsgLengthToDlc(uint length);

    [DllImport(DllName, EntryPoint = "CANTP_StatusListTypes_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern PCanTpStatusType StatusListTypes(PCanTpStatus status);

    [DllImport(DllName, EntryPoint = "CANTP_StatusGet_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    public static extern uint StatusGet(PCanTpStatus status, PCanTpStatusType type);

    [DllImport(DllName, EntryPoint = "CANTP_StatusIsOk_2016", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool StatusIsOk(PCanTpStatus status, PCanTpStatus expected, [MarshalAs(UnmanagedType.I1)] bool strict);

}
