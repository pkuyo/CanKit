using System;

namespace CanKit.Core.Definitions;


/// <summary>
/// Timing configuration for a single CAN phase.
/// ZH: 单一 CAN 相位的时序配置。
/// </summary>
/// <param name="Bitrate">Bitrate in bps (比特率，bps)。</param>
/// <param name="SamplePointPermille">Sample point in permille, e.g. 875=87.5%（千分数，可选）。</param>
/// <param name="SjwTq">Sync jump width in TQ（同步跳转宽度，单位 TQ，可选）。</param>
public readonly record struct CanTimingConfig(
    uint? Bitrate = null,
    uint? SamplePointPermille = null,
    uint? SjwTq = null,
    AdvancedBittiming? Segment = null)
{
    public bool UseBitRate => Bitrate != null;

    public bool UseBitRateWithSamplePoint => UseBitRate && SamplePointPermille != null;

    public bool UseSegment => Segment != null;

}

/// <summary>
/// Timing configuration for CAN FD with nominal (arbitration) and data phases.
/// ZH: CAN FD 时序配置（仲裁与数据相位）。
/// </summary>
public readonly record struct CanFdTimingConfig(
    CanTimingConfig Nominal,
    CanTimingConfig Data);

/// <summary>
/// Advanced low-level bit timing segments.
/// ZH: 高级位时序分段（直接指定 BRP/TSEG/SJW/TQ）。
/// </summary>
public readonly record struct AdvancedBittiming(
    uint Brp,
    uint PropSeg,
    uint PhaseSeg1,
    uint PhaseSeg2,
    uint Sjw,
    uint? TqNs = null);

/// <summary>
/// Unified CAN bit timing holder.
/// ZH: 统一的 CAN 位时序配置。
/// </summary>
public readonly record struct BitTiming(
    CanTimingConfig? Classic,
    CanFdTimingConfig? Fd);

/// <summary>
/// Parameters for sending CAN data.
/// ZH: 发送 CAN 数据的参数。
/// </summary>
public record CanTransmitData(ICanFrame CanFrame);

/// <summary>
/// Represents a received CAN data event.
/// ZH: 接收的 CAN 数据事件。
/// </summary>
public record CanReceiveData(ICanFrame CanFrame)
{
    /// <summary>
    /// Received frame.
    /// ZH: 接收到的帧。
    /// </summary>
    public ICanFrame CanFrame = CanFrame;

    /// <summary>
    /// Device-provided timestamp (usually hardware).
    /// ZH: 设备提供的时间戳（通常来自硬件）。
    /// </summary>
    public UInt64 recvTimestamp;

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
public record CanErrorCounters
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
}

