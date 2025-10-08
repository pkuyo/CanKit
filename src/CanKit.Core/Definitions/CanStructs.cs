using System;

namespace CanKit.Core.Definitions;

/// <summary>
/// Timing configuration for a single CAN phase. (单一 CAN 相位的时序配置。)
/// </summary>
public readonly record struct CanPhaseTiming
{
    private CanPhaseTiming(uint bitrate, ushort? samplePointPermille, BitTimingSegments? segments)
    {
        Bitrate = bitrate == 0 ? null : bitrate;
        SamplePointPermille = samplePointPermille;
        Segments = segments;
    }

    /// <summary>
    /// Indicates this phase is specified by target bitrate / sample point. (指示该相位通过目标比特率/采样点指定。)
    /// </summary>
    public bool IsTarget => Bitrate.HasValue;

    /// <summary>
    /// Indicates this phase is specified by low-level segments. (指示该相位通过底层分段参数指定。)
    /// </summary>
    public bool IsSegment => Segments != null;

    /// <summary>
    /// Bitrate in bps. (比特率，单位 bps。)
    /// </summary>
    public uint? Bitrate { get; init; }

    /// <summary>
    /// Sample point in permille (e.g., 875 = 87.5%). (采样点，千分数表示，例如 875 代表 87.5%。)
    /// </summary>
    public ushort? SamplePointPermille { get; init; }

    /// <summary>
    /// Explicit bit timing segments (BRP/TSEG/SJW). (显式位时序分段参数（BRP/TSEG/SJW）。)
    /// </summary>
    public BitTimingSegments? Segments { get; init; }

    /// <summary>
    /// Create timing from a target bitrate (optional sample point). (从目标比特率创建时序，采样点可选。)
    /// </summary>
    public static CanPhaseTiming Target(uint bitrate, ushort? samplePointPermille = null)
        => new(bitrate, samplePointPermille, null);

    /// <summary>
    /// Create timing from low-level segments. (从底层分段参数创建时序。)
    /// </summary>
    public static CanPhaseTiming FromSegments(BitTimingSegments segments)
        => new(0, null, segments);
}

/// <summary>
/// Timing configuration for CAN FD with nominal (arbitration) and data phases. (CAN FD 时序配置，包含仲裁与数据两个相位。)
/// </summary>
/// <param name="Nominal">Nominal (arbitration) phase timing. (仲裁相位时序。)</param>
/// <param name="Data">Data phase timing. (数据相位时序。)</param>
/// <param name="clockMHz">Reference clock in MHz. (参考时钟，单位 MHz。)</param>
public readonly record struct CanFdTiming(
    CanPhaseTiming Nominal,
    CanPhaseTiming Data,
    uint? clockMHz);

/// <summary>
/// Timing configuration for Classical CAN. (经典 CAN 的时序配置。)
/// </summary>
/// <param name="Nominal">Nominal (single) phase timing. (单一仲裁相位时序。)</param>
/// <param name="clockMHz">Reference clock in MHz. (参考时钟，单位 MHz。)</param>
public readonly record struct CanClassicTiming(
    CanPhaseTiming Nominal,
    uint? clockMHz);

/// <summary>
/// Advanced low-level bit timing segments (explicit BRP/TSEG/SJW/TQ). (高级位时序分段，直接指定 BRP/TSEG/SJW/TQ。)
/// </summary>
/// <param name="Brp">Bit-rate prescaler. (比特率分频器 BRP。)</param>
/// <param name="Tseg1">Time segment 1 (in TQ). (时间段 1，单位 TQ。)</param>
/// <param name="Tseg2">Time segment 2 (in TQ). (时间段 2，单位 TQ。)</param>
/// <param name="Sjw">Synchronization jump width (in TQ). (同步跳转宽度 SJW，单位 TQ。)</param>
public readonly record struct BitTimingSegments(
    uint Brp,
    uint Tseg1,
    uint Tseg2,
    uint Sjw)
{
    /// <summary>
    /// Number of time quanta per bit (Ntq = 1 + TSEG1 + TSEG2). (每比特的时间量化数 Ntq = 1 + TSEG1 + TSEG2。)
    /// </summary>
    public uint Ntq => 1 + Tseg1 + Tseg2;

    /// <summary>
    /// Sample point in permille: (1 + TSEG1) / Ntq * 1000. (采样点千分比：(1 + TSEG1) / Ntq * 1000。)
    /// </summary>
    public uint SamplePointPermille => (1 + Tseg1) * 1000 / Ntq;

    /// <summary>
    /// String representation for debugging. (用于调试的字符串表示。)
    /// </summary>
    public override string ToString() =>
        $"BRP={Brp}, TSEG1={Tseg1}, TSEG2={Tseg2}, SJW={Sjw}";

    /// <summary>
    /// Compute bitrate (bps) from reference clock (MHz). (根据参考时钟 MHz 计算比特率 bps。)
    /// </summary>
    public uint BitRate(uint clockMHz) => (uint)(clockMHz * 1_000_000 / (Brp * Ntq));
}

/// <summary>
/// Hardware/driver bit timing limits. (硬件/驱动的位时序限制。)
/// </summary>
public sealed record BitTimingLimits
{
    /// <summary>
    /// Minimum time quanta per bit. (每比特最小 TQ 数。)
    /// </summary>
    public int NtqMin { get; init; } = 8;

    /// <summary>
    /// Maximum time quanta per bit. (每比特最大 TQ 数。)
    /// </summary>
    public int NtqMax { get; init; } = 40;

    /// <summary>
    /// Minimum BRP. (最小 BRP 值。)
    /// </summary>
    public int BrpMin { get; init; } = 1;

    /// <summary>
    /// Maximum BRP. (最大 BRP 值。)
    /// </summary>
    public int BrpMax { get; init; } = 1024;

    /// <summary>
    /// Minimum TSEG1 (in TQ). (TSEG1 最小值，单位 TQ。)
    /// </summary>
    public int Tseg1Min { get; init; } = 1;

    /// <summary>
    /// Maximum TSEG1 (in TQ). (TSEG1 最大值，单位 TQ。)
    /// </summary>
    public int Tseg1Max { get; init; } = 256;

    /// <summary>
    /// Minimum TSEG2 (in TQ). (TSEG2 最小值，单位 TQ。)
    /// </summary>
    public int Tseg2Min { get; init; } = 1;

    /// <summary>
    /// Maximum TSEG2 (in TQ). (TSEG2 最大值，单位 TQ。)
    /// </summary>
    public int Tseg2Max { get; init; } = 128;

    /// <summary>
    /// Minimum SJW (in TQ). (SJW 最小值，单位 TQ。)
    /// </summary>
    public int SjwMin { get; init; } = 1;

    /// <summary>
    /// Maximum SJW (in TQ). (SJW 最大值，单位 TQ。)
    /// </summary>
    public int SjwMax { get; init; } = 128;

    /// <summary>
    /// Prefer solutions with larger Ntq when multiple are tied. (当解相同优先选择更大的 Ntq。)
    /// </summary>
    public bool PreferLargerNtqWhenTied { get; init; } = true;
}

/// <summary>
/// Unified CAN bit timing holder. (统一的 CAN 位时序配置容器。)
/// </summary>
public readonly record struct CanBusTiming
{
    /// <summary>
    /// Create holder for Classical CAN timing. (为经典 CAN 时序创建容器。)
    /// </summary>
    public CanBusTiming(CanClassicTiming classic) => Classic = classic;

    /// <summary>
    /// Create holder for CAN FD timing. (为 CAN FD 时序创建容器。)
    /// </summary>
    public CanBusTiming(CanFdTiming fd) => Fd = fd;

    /// <summary>
    /// Classical CAN timing. (经典 CAN 时序。)
    /// </summary>
    public CanClassicTiming? Classic { get; init; }

    /// <summary>
    /// CAN FD timing. (CAN FD 时序。)
    /// </summary>
    public CanFdTiming? Fd { get; init; }

    /// <summary>
    /// Create default Classical CAN timing (default 500 kbps). (创建默认经典 CAN 时序（默认 500 kbps）。)
    /// </summary>
    public static CanBusTiming ClassicDefault(uint bitRate = 500_000)
        => new(new CanClassicTiming(CanPhaseTiming.Target(bitRate), null));

    /// <summary>
    /// Create default CAN FD timing (both phases default 500 kbps). (创建默认 CAN FD 时序（仲裁与数据均为 500 kbps）。)
    /// </summary>
    public static CanBusTiming FdDefault(uint bitRate = 500_000, uint dBitRate = 500_000)
        => new(new CanFdTiming(CanPhaseTiming.Target(bitRate), CanPhaseTiming.Target(dBitRate), null));
}

/// <summary>
/// Parameters for sending CAN data. (发送 CAN 数据的参数。)
/// </summary>
public readonly record struct CanTransmitData(
    /// <summary>Frame to transmit. (待发送的帧。)</summary>
    ICanFrame CanFrame);

/// <summary>
/// Represents a received CAN data event. (接收的 CAN 数据事件。)
/// </summary>
public readonly record struct CanReceiveData(ICanFrame CanFrame)
{
    /// <summary>
    /// Received frame. (接收到的帧。)
    /// </summary>
    public ICanFrame CanFrame { get; } = CanFrame;

    /// <summary>
    /// Device-provided timestamp (usually from hardware). (设备提供的时间戳（通常来自硬件）。)
    /// </summary>
    public TimeSpan ReceiveTimestamp { get; init; }

    /// <summary>
    /// System time corresponding to this record. (该记录对应的系统时间。)
    /// </summary>
    public DateTime SystemTimestamp { get; } = DateTime.Now;
}

/// <summary>
/// CAN bus error counters. (CAN 总线错误计数器。)
/// </summary>
public readonly record struct CanErrorCounters
{
    /// <summary>
    /// Transmit error counter (TEC). (发送错误计数 TEC。)
    /// </summary>
    public int TransmitErrorCounter { get; init; }

    /// <summary>
    /// Receive error counter (REC). (接收错误计数 REC。)
    /// </summary>
    public int ReceiveErrorCounter { get; init; }

    /// <summary>
    /// Deconstruct into TEC and REC. (解构为 TEC 与 REC。)
    /// </summary>
    public void Deconstruct(out int TransmitErrorCounter, out int ReceiveErrorCounter)
    {
        TransmitErrorCounter = this.TransmitErrorCounter;
        ReceiveErrorCounter = this.ReceiveErrorCounter;
    }

    /// <summary>
    /// Human-readable counters string. (可读的计数字符串。)
    /// </summary>
    public override string ToString() => $"TEC:{TransmitErrorCounter}, REC:{ReceiveErrorCounter}";
}
