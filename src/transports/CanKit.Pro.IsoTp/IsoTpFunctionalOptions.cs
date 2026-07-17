using System;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// Configuration for an <see cref="IsoTpFunctionalClient"/>.
/// </summary>
/// <remarks>
/// Functional (1:N) addressing uses Normal ISO-TP addressing (no address-extension byte) and
/// restricts outbound messages to Single Frames only (ISO 15765-2 §9 / ISO 14229-1 §7.5.4).
/// Options that only apply to multi-frame sessions (N_Bs, N_Cr, WftMax, LocalBlockSize,
/// LocalStMin) are therefore absent.
/// </remarks>
public sealed class IsoTpFunctionalOptions
{
    /// <summary>Default N_As TX-confirm timeout (1 second, matching <see cref="IsoTpChannelOptions.DefaultTimeout"/>).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// <c>true</c> when the functional TX CAN identifier and the response-range CAN identifiers
    /// are 29-bit extended; <c>false</c> for 11-bit standard identifiers. Defaults to
    /// <c>false</c>.
    /// </summary>
    public bool IsExtendedCanId { get; init; }

    /// <summary>
    /// <c>true</c> to build the outbound Single Frame as a CAN-FD frame (up to 62 user bytes in
    /// the escape form); <c>false</c> for classic CAN (up to 7 user bytes). Must match the
    /// underlying bus mode. Defaults to <c>false</c>.
    /// </summary>
    public bool UseCanFd { get; init; }

    /// <summary>
    /// <c>true</c> to pad the outbound Single Frame to the next valid CAN/CAN-FD DLC step.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool UsePadding { get; init; } = true;

    /// <summary>
    /// Byte used to pad the outbound Single Frame when <see cref="UsePadding"/> is
    /// <c>true</c>. Defaults to <see cref="IsoTpFrameCodec.DefaultPaddingByte"/> (<c>0xCC</c>).
    /// </summary>
    public byte PaddingByte { get; init; } = IsoTpFrameCodec.DefaultPaddingByte;

    /// <summary>
    /// N_As — maximum time the sender waits for the CAN driver to confirm the Single Frame was
    /// accepted. Defaults to <see cref="DefaultTimeout"/> (1 second).
    /// </summary>
    public TimeSpan NAs { get; init; } = DefaultTimeout;
}
