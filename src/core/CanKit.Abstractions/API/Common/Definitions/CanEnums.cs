using System;

#pragma warning disable IDE0055
namespace CanKit.Abstractions.API.Common.Definitions
{
    /// <summary>
    /// Frame error types (错误类型，支持按位组合)
    /// </summary>
    [Flags]
    public enum FrameErrorType : uint
    {
        None = 0,

        TxTimeout         = 1 << 0,
        ArbitrationLost   = 1 << 1,
        Controller        = 1 << 2,
        ProtocolViolation = 1 << 3,
        TransceiverError  = 1 << 4,
        AckError          = 1 << 5,
        BusOff            = 1 << 6,
        BusError          = 1 << 7,
        Restarted         = 1 << 8,


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
    /// Protocol violation type (协议违规类型)
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
        Reserved             = 249,   // 规范保留值
        VendorSpecific       = 250,

        Invalid              = 255,
    }

    /// <summary>
    /// Transceiver status code (通道状态码)
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
    public enum CanFrameType
    {
        Invalid = 0,
        Can20   = 1,
        CanFd   = 2,
        CanXl   = 3,
    }

    /// <summary>
    /// Supported CAN protocol modes (支持的协议模式)
    /// </summary>
    public enum CanProtocolMode
    {
        Invalid = 0,
        Can20   = 1,
        CanFd   = 2,
        CanXl   = 3,
    }

    /// <summary>
    /// Device capability flags (设备功能标志)
    /// </summary>
    [Flags]
    public enum CanFeature
    {
        CanClassic    = 1 << 0,
        CanFd         = 1 << 1,
        MaskFilter    = 1 << 2,
        CyclicTx      = 1 << 3,
        BusUsage      = 1 << 4,
        ListenOnly    = 1 << 5,
        Echo          = 1 << 6,
        ErrorCounters = 1 << 7,
        ErrorFrame    = 1 << 8,
        TxRetryPolicy = 1 << 9,
        RangeFilter   = 1 << 10,
        TxTimeOut     = 1 << 11,
        Filters       = MaskFilter | RangeFilter,
        All           = int.MaxValue,
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
        Standard = 0,
        Extend = 1,

        //Only use in some SJA1000 device
        Double = 0,
        Single = 1,
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
