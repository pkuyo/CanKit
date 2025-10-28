using System;
using CanKit.Core.Definitions;

namespace CanKit.Protocol.IsoTp;

/// <summary>
/// ISO-TP link settings (标准 ISO-TP 设置)
/// </summary>
public sealed class IsoTpSettings
{
    /// <summary>
    /// Transmit CAN ID (local->remote). (发送 ID)
    /// </summary>
    public int TxId { get; set; }

    /// <summary>
    /// Receive CAN ID (remote->local). (接收 ID)
    /// </summary>
    public int RxId { get; set; }

    /// <summary>
    /// Use 29-bit extended IDs. (是否为扩展帧)
    /// </summary>
    public bool IsExtendedId { get; set; }

    /// <summary>
    /// Optional extended addressing byte. If set, the first data byte is this value.
    /// （可选扩展地址字节，若设置则作为首个数据字节）
    /// </summary>
    public byte? ExtendedAddress { get; set; }

    /// <summary>
    /// Force padding of TX frames to the DLC (usually 8). (是否填充到 DLC)
    /// </summary>
    public bool PadFrames { get; set; } = true;

    /// <summary>
    /// Padding byte value if <see cref="PadFrames"/> is true. (填充字节)
    /// </summary>
    public byte PadByte { get; set; } = 0x00;

    /// <summary>
    /// Default Block Size when we are the receiver (for FC). 0 means unlimited. (作为接收端发送 FC 时的缺省 BS；0 表示不限)
    /// </summary>
    public byte BlockSize { get; set; } = 0x00;

    /// <summary>
    /// Default STmin when we are the receiver (ms; 0-0x7F). (作为接收端发送 FC 时的缺省 STmin，毫秒)
    /// </summary>
    public byte STmin { get; set; } = 0x00;

    /// <summary>
    /// Max number of WAIT flow controls to accept during TX before abort. (发送侧 WAIT 最多接受次数)
    /// </summary>
    public int WftMax { get; set; } = 3;

    /// <summary>
    /// N_As: timeout for sending any frame (ms). (发送帧超时 N_As)
    /// </summary>
    public int N_As { get; set; } = 1000;

    /// <summary>
    /// N_Bs: timeout waiting for Flow Control (ms). (等待 FC 的超时 N_Bs)
    /// </summary>
    public int N_Bs { get; set; } = 1000;

    /// <summary>
    /// N_Cr: timeout waiting for Consecutive Frame (ms). (等待 CF 的超时 N_Cr)
    /// </summary>
    public int N_Cr { get; set; } = 1000;

    /// <summary>
    /// Maximum reassembled message size (bytes). (最大重组长度)
    /// </summary>
    public int MaxMessageSize { get; set; } = 4096;

    /// <summary>
    /// DLC size for classical CAN, default 8. (经典 CAN 的 DLC，默认 8)
    /// </summary>
    public int ClassicalDlc { get; set; } = 8;

    /// <summary>
    /// Use CAN FD frames for ISO-TP. (是否启用 CAN FD)
    /// </summary>
    public bool UseFd { get; set; } = false;

    /// <summary>
    /// DLC size for CAN FD, default 64. (CAN FD 的 DLC，默认 64)
    /// </summary>
    public int FdDlc { get; set; } = 64;

    /// <summary>
    /// Enable Bit Rate Switching (BRS) for FD frames. (FD 帧是否启用 BRS)
    /// </summary>
    public bool FdBitRateSwitch { get; set; } = false;

    /// <summary>
    /// Allow extended FF length (CAN FD large length field). (允许 FF 扩展长度字段)
    /// </summary>
    public bool AllowLargeFdLength { get; set; } = true;

    internal CanFilterIDType IdType => IsExtendedId ? CanFilterIDType.Extend : CanFilterIDType.Standard;
}
