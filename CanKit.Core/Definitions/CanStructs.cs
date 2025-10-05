using System;

namespace CanKit.Core.Definitions;

/// <summary>
/// Timing configuration for a single CAN phase.
/// ZH: 单一 CAN 相位的时序配置。
/// </summary>
public readonly record struct CanPhaseTiming
{
    private CanPhaseTiming(uint bitrate, ushort? samplePointPermille, BitTimingSegments? segments)
    {
        Bitrate = bitrate == 0 ? null : bitrate;
        SamplePointPermille = samplePointPermille;
        Segments = segments;
    }

    public bool IsTarget => Bitrate.HasValue;
    public bool IsSegment => Segments != null;

    /// <summary>Bitrate in bps (比特率，bps)。</summary>
    public uint? Bitrate { get; init; }

    /// <summary>Sample point in permille, e.g. 875=87.5%（千分数，可选）。</summary>
    public ushort? SamplePointPermille { get; init; }

    public BitTimingSegments? Segments { get; init; }

    public static CanPhaseTiming Target(uint bitrate, ushort? samplePointPermille = null)
        => new(bitrate, samplePointPermille, null);

    public static CanPhaseTiming FromSegments(BitTimingSegments segments)
        => new(0, null, segments);
}

/// <summary>
/// Timing configuration for CAN FD with nominal (arbitration) and data phases.
/// ZH: CAN FD 时序配置（仲裁与数据相位）。
/// </summary>
public readonly record struct CanFdTiming(
    CanPhaseTiming Nominal,
    CanPhaseTiming Data,
    uint? clockMHz);

public readonly record struct CanClassicTiming(
    CanPhaseTiming Nominal,
    uint? clockMHz);

/// <summary>
/// Advanced low-level bit timing segments.
/// ZH: 高级位时序分段（直接指定 BRP/TSEG/SJW/TQ）。
/// </summary>
public readonly record struct BitTimingSegments(
    uint Brp,
    uint Tseg1,
    uint Tseg2,
    uint Sjw)
{
    public uint Ntq => 1 + Tseg1 + Tseg2;

    public uint SamplePointPermille => (1 + Tseg1) * 1000 / Ntq;

    public override string ToString() =>
        $"BRP={Brp}, TSEG1={Tseg1}, TSEG2={Tseg2}, SJW={Sjw}";

    public uint BitRate(uint clockMHz) => (uint)(clockMHz * 1_000_000 / (Brp * Ntq));
}

public sealed record BitTimingLimits
{
    public int NtqMin { get; init; } = 8;
    public int NtqMax { get; init; } = 40;

    public int BrpMin { get; init; } = 1;
    public int BrpMax { get; init; } = 1024;

    public int Tseg1Min { get; init; } = 1;
    public int Tseg1Max { get; init; } = 256;

    public int Tseg2Min { get; init; } = 1;
    public int Tseg2Max { get; init; } = 128;

    public int SjwMin { get; init; } = 1;
    public int SjwMax { get; init; } = 128;

    public bool PreferLargerNtqWhenTied { get; init; } = true;
}

/// <summary>
/// Unified CAN bit timing holder.
/// ZH: 统一的 CAN 位时序配置。
/// </summary>
public readonly record struct CanBusTiming
{
    public CanBusTiming(CanClassicTiming classic) => Classic = classic;

    public CanBusTiming(CanFdTiming fd) => Fd = fd;

    public CanClassicTiming? Classic { get; init; }
    public CanFdTiming? Fd { get; init; }

    public static CanBusTiming ClassicDefault(uint bitRate = 500_000)
        => new(new CanClassicTiming(CanPhaseTiming.Target(bitRate), null));

    public static CanBusTiming FdDefault(uint bitRate = 500_000, uint dBitRate = 500_000)
        => new(new CanFdTiming(CanPhaseTiming.Target(bitRate), CanPhaseTiming.Target(dBitRate), null));
}

/// <summary>
/// Parameters for sending CAN data.
/// ZH: 发送 CAN 数据的参数。
/// </summary>
public readonly record struct CanTransmitData(ICanFrame CanFrame);

/// <summary>
/// Represents a received CAN data event.
/// ZH: 接收的 CAN 数据事件。
/// </summary>
public readonly record struct CanReceiveData(ICanFrame CanFrame)
{
    /// <summary>
    /// Received frame.
    /// ZH: 接收到的帧。
    /// </summary>
    public ICanFrame CanFrame { get; } = CanFrame;

    /// <summary>
    /// Device-provided timestamp (usually hardware).
    /// ZH: 设备提供的时间戳（通常来自硬件）。
    /// </summary>
    public TimeSpan ReceiveTimestamp { get; init; }

    /// <summary>
    /// System time corresponding to the record.
    /// ZH: 记录对应的系统时间。
    /// </summary>
    public DateTime SystemTimestamp { get; } = DateTime.Now;
}

/// <summary>
/// CAN bus error counters.
/// ZH: CAN 总线错误计数器。
/// </summary>
public readonly record struct CanErrorCounters
{
    /// <summary>
    /// Transmit error counter (TEC).
    /// ZH: 发送错误计数（TEC）。
    /// </summary>
    public int TransmitErrorCounter { get; init; }

    /// <summary>
    /// Receive error counter (REC).
    /// ZH: 接收错误计数（REC）。
    /// </summary>
    public int ReceiveErrorCounter { get; init; }

    public void Deconstruct(out int TransmitErrorCounter, out int ReceiveErrorCounter)
    {
        TransmitErrorCounter = this.TransmitErrorCounter;
        ReceiveErrorCounter = this.ReceiveErrorCounter;
    }

    public override string ToString() => $"TEC:{TransmitErrorCounter}, REC:{ReceiveErrorCounter}";
}

