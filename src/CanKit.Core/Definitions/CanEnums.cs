using System;
#pragma warning disable IDE0055
namespace CanKit.Core.Definitions
{
    /// <summary>
    /// Frame error types (错误类型，支持按位组合)
    /// </summary>
    [Flags]
    public enum FrameErrorType : uint
    {
        None = 0,

        // Top-level categories aligned with SocketCAN CAN_ERR_* classes
        // 这些位对应 arbitration_id 上的 CAN_ERR_FLAG 类别；更细节见其它字段
        TxTimeout         = 1 << 0, // CAN_ERR_TX_TIMEOUT
        ArbitrationLost   = 1 << 1, // CAN_ERR_LOSTARB
        Controller        = 1 << 2, // CAN_ERR_CRTL（细节见 CanControllerStatus）
        ProtocolViolation = 1 << 3, // CAN_ERR_PROT（细节见 CanProtocolViolationType / FrameErrorLocation）
        TransceiverError  = 1 << 4, // CAN_ERR_TRX（细节见 CanTransceiverStatus）
        AckError          = 1 << 5, // CAN_ERR_ACK
        BusOff            = 1 << 6, // CAN_ERR_BUSOFF
        BusError          = 1 << 7, // CAN_ERR_BUSERROR
        Restarted         = 1 << 8, // CAN_ERR_RESTARTED

        // 非错误帧（设备/驱动）级别错误，保留用于设备报告
        DeviceError       = 1 << 17,
        DriverError       = 1 << 18,
        ResourceError     = 1 << 19,
        CommandFailed     = 1 << 20,

        Unknown           = 1u << 30,
    }

    /// <summary>
    /// Controller status details (控制器状态，来源于设备/驱动或错误帧 Data[1])
    /// </summary>
    [Flags]
    public enum CanControllerStatus : byte
    {
        None = 0,
        RxOverflow = 1 << 0,
        TxOverflow = 1 << 1,
        RxWarning  = 1 << 2,
        TxWarning  = 1 << 3,
        RxPassive  = 1 << 4,
        TxPassive  = 1 << 5,
        Active     = 1 << 6,
        Unknown    = 1 << 7,
    }

    /// <summary>
    /// Protocol violation type
    /// </summary>
    [Flags]
    public enum CanProtocolViolationType : UInt16
    {
        None    = 0,
        Bit     = 1 << 0,
        Form    = 1 << 1,
        Stuff   = 1 << 2,
        Bit0    = 1 << 3,
        Bit1    = 1 << 4,
        Overload= 1 << 5,
        Active  = 1 << 6,
        Tx      = 1 << 7,
        Unknown = 1 << 8,
    }

    /// <summary>
    /// Error location within a CAN frame (错误发生的位置)
    /// </summary>
    public enum FrameErrorLocation : byte
    {
        // 0..31:
        Unspecified          = 0,
        StartOfFrame         = 1,
        Identifier           = 2,
        SRTR                 = 3,
        IDE                  = 4,
        RTR                  = 5,
        DLC                  = 6,
        DataField            = 7,
        CRCSequence          = 8,
        CRCDelimiter         = 9,
        AckSlot              = 10,
        AckDelimiter         = 11,
        EndOfFrame           = 12,
        Intermission         = 13,

        // 32..63:
        ActiveErrorFlag      = 32,
        PassiveErrorFlag     = 33,
        ErrorDelimiter       = 34,
        OverloadFlag         = 35,
        TolerateDominantBits = 36,

        // 240..255:
        Unrecognized         = 248,   // 合法范围内但未收录
        Reserved             = 249,       // 规范保留值
        VendorSpecific       = 250,

        Invalid              = 255,
    }

    /// <summary>
    /// Transceiver status code
    /// </summary>
    public enum CanTransceiverStatus : byte
    {
        Unspecified           = 0x00,
        CanHNoWire            = 0x04,
        CanHShortToBat        = 0x05,
        CanHShortToVcc        = 0x06,
        CanHShortToGnd        = 0x07,
        CanLNoWire            = 0x40,
        CanLShortToBat        = 0x50,
        CanLShortToVcc        = 0x60,
        CanLShortToGnd        = 0x70,
        CanLShortToCanH       = 0x80,

        Unknown               = 0xFF,
    }

    /// <summary>
    /// Frame direction: Tx or Rx (帧方向：发送或接收)
    /// </summary>
    public enum FrameDirection
    {
        Unknown = 0,
        Tx,
        Rx
    }



    /// <summary>
    /// Frame type flags used to identify frame kind (帧类型标志)
    /// </summary>
    [Flags]
    public enum CanFrameType : uint
    {
        Invalid = 0,
        Can20   = 1 << 0,
        CanFd   = 1 << 1,
        Any     = int.MaxValue
    }

    /// <summary>
    /// Supported CAN protocol modes (支持的协议模式)
    /// </summary>
    public enum CanProtocolMode
    {
        Can20 = 0,
        CanFd = 1
    }

    /// <summary>
    /// Device capability flags (设备功能标志)
    /// </summary>
    [Flags]
    public enum CanFeature
    {
        CanClassic    = 1 << 0,
        CanFd         = 1 << 1,
        Filters       = 1 << 2,
        CyclicTx      = 1 << 3,
        BusUsage      = 1 << 4,
        ListenOnly    = 1 << 5,
        Echo          = 1 << 6,
        ErrorCounters = 1 << 7,
        ErrorFrame    = 1 << 8,
        TxRetryPolicy = 1 << 9,
        All           = int.MaxValue,
    }

    /// <summary>
    /// Options application phase (选项应用阶段)
    /// </summary>
    public enum CanOptionType
    {
        Init    = 1,
        Runtime = 2
    }

    /// <summary>
    /// TX retry policies (发送重试策略)
    /// </summary>
    public enum TxRetryPolicy : byte
    {
        AlwaysRetry = 0,
        NoRetry     = 1,
    }

    /// <summary>
    /// Channel work mode (通道工作模式)
    /// </summary>
    public enum ChannelWorkMode : byte
    {
        Normal     = 0,
        ListenOnly = 1,
        Echo       = 2,
    }

    /// <summary>
    /// Filter ID type: standard or extended (过滤 ID 类型：标/扩展)
    /// </summary>
    public enum CanFilterIDType
    {
        Standard,
        Extend,
    }

    /// <summary>
    /// CAN Bus state (CAN总线错误状态)
    /// </summary>
    public enum BusState
    {
        None       = 0,
        ErrActive  = 1,
        ErrWarning = 2,
        ErrPassive = 3,
        BusOff     = 4,

        Unknown     = int.MaxValue
    }
}
