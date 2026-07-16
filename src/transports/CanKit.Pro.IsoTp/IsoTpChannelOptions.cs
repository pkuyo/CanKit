using System;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// Configuration for an <see cref="IIsoTpChannel"/>.
/// </summary>
/// <remarks>
/// All values are set at channel creation time and treated as immutable for the channel's
/// lifetime. Timeouts follow ISO 15765-2 §6.5 naming and their defaults are intentionally
/// conservative (1 second each) so tests and virtual-loopback sessions never sit for the
/// standard's much larger recommended values (e.g. 1000 ms N_As) when the transport itself is
/// only microseconds slow. Production callers are expected to override the timing values that
/// matter for their bus.
/// </remarks>
public sealed class IsoTpChannelOptions
{
    /// <summary>Default value for <see cref="NAs"/>, <see cref="NBs"/>, <see cref="NCr"/>.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// <c>true</c> to build ISO-TP frames as CAN-FD (up to 64-byte per frame); <c>false</c> for
    /// classic CAN (8-byte per frame). Must match the underlying <see cref="Abstractions.API.Can.ICanBus"/>
    /// mode; if the bus is classic-CAN, <c>true</c> here will produce frames the driver rejects.
    /// </summary>
    public bool UseCanFd { get; init; }

    /// <summary>
    /// <c>true</c> to pad each frame to the next valid CAN/CAN-FD DLC step with
    /// <see cref="PaddingByte"/>; <c>false</c> to send the exact minimum payload. Many ISO 15765-2
    /// stacks expect padding on classic CAN; CAN-FD padding is optional per ISO 15765-2 §5.
    /// </summary>
    public bool UsePadding { get; init; } = true;

    /// <summary>
    /// Byte used to pad SF/CF/FC frames when <see cref="UsePadding"/> is <c>true</c>. Defaults to
    /// <see cref="IsoTpFrameCodec.DefaultPaddingByte"/> (<c>0xCC</c>).
    /// </summary>
    public byte PaddingByte { get; init; } = IsoTpFrameCodec.DefaultPaddingByte;

    /// <summary>
    /// Block size (BS) advertised on Flow-Control frames sent by this channel while receiving
    /// multi-frame PDUs. <c>0</c> means "no additional FCs needed, send all remaining CFs".
    /// </summary>
    public byte LocalBlockSize { get; init; }

    /// <summary>
    /// STmin advertised on Flow-Control frames sent by this channel while receiving multi-frame
    /// PDUs. Encoded per ISO 15765-2 (0..127 ms in 1-ms steps, 100..900 µs in 100-µs steps).
    /// Defaults to <see cref="TimeSpan.Zero"/> (no minimum separation requested).
    /// </summary>
    public TimeSpan LocalStMin { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// N_As — maximum time between the sender handing an SF/FF/CF to the driver and the driver
    /// confirming it was sent (ISO 15765-2 §6.5, corresponds to the FR-TP-010 acceptance
    /// criterion). Modelled here as the TX-confirmation timeout the channel uses when calling
    /// <see cref="RawCan.ICanBusService.SendConfirmed"/>. Defaults to <see cref="DefaultTimeout"/>.
    /// </summary>
    public TimeSpan NAs { get; init; } = DefaultTimeout;

    /// <summary>
    /// N_Bs — maximum time between the sender's FF/last-CF-of-a-block and the peer's Flow Control
    /// (ISO 15765-2 §6.5). Expiring aborts the outgoing PDU with a timeout error (FR-TP-010).
    /// Defaults to <see cref="DefaultTimeout"/>.
    /// </summary>
    public TimeSpan NBs { get; init; } = DefaultTimeout;

    /// <summary>
    /// N_Cr — maximum time between two Consecutive Frames on the receive side (ISO 15765-2 §6.5).
    /// Expiring aborts the incoming reassembly (FR-TP-010). Defaults to <see cref="DefaultTimeout"/>.
    /// </summary>
    public TimeSpan NCr { get; init; } = DefaultTimeout;

    /// <summary>
    /// WFTmax — maximum number of consecutive <see cref="FlowStatus.Wait"/> FCs the sender is
    /// willing to accept before aborting (ISO 15765-2 §6.3, FR-TP-011). Defaults to 10.
    /// </summary>
    public int WftMax { get; init; } = 10;

    /// <summary>
    /// Bounded capacity of the internal receive buffer that holds fully reassembled PDUs waiting
    /// for the consumer. When full, the oldest PDU is dropped so the RX loop never stalls.
    /// Defaults to 64.
    /// </summary>
    public int ReceiveBufferCapacity { get; init; } = 64;

    /// <summary>
    /// Convenience clone that returns a new instance with the provided overrides. Useful for
    /// tests that want to tweak one field of a shared default template.
    /// </summary>
    public IsoTpChannelOptions With(
        bool? useCanFd = null,
        bool? usePadding = null,
        byte? paddingByte = null,
        byte? localBlockSize = null,
        TimeSpan? localStMin = null,
        TimeSpan? nAs = null,
        TimeSpan? nBs = null,
        TimeSpan? nCr = null,
        int? wftMax = null,
        int? receiveBufferCapacity = null)
        => new()
        {
            UseCanFd = useCanFd ?? UseCanFd,
            UsePadding = usePadding ?? UsePadding,
            PaddingByte = paddingByte ?? PaddingByte,
            LocalBlockSize = localBlockSize ?? LocalBlockSize,
            LocalStMin = localStMin ?? LocalStMin,
            NAs = nAs ?? NAs,
            NBs = nBs ?? NBs,
            NCr = nCr ?? NCr,
            WftMax = wftMax ?? WftMax,
            ReceiveBufferCapacity = receiveBufferCapacity ?? ReceiveBufferCapacity,
        };
}
