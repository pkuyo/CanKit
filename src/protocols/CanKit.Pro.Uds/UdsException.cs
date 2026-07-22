using System;
using CanKit.Core.Exceptions;

namespace CanKit.Pro.Uds;

/// <summary>
/// Base type for every error surfaced by <see cref="IUdsClient"/>. Subclasses distinguish the
/// three families the MVP cares about: a well-formed negative response from the ECU
/// (<see cref="UdsNegativeResponseException"/>), an ECU that failed to respond within its P2/P2*
/// budget (<see cref="UdsTimeoutException"/>), and a malformed / mismatched response
/// (<see cref="UdsProtocolException"/>). Transport-layer failures are re-thrown as-is (they are
/// already typed as <see cref="IsoTp.IsoTpException"/> subclasses). Derives from
/// <see cref="CanKitException"/> so L2/L3/L4 failures can be caught uniformly across the
/// library (NFR-006 error architecture, arc42 ADR-12).
/// </summary>
public class UdsException : CanKitException
{
    /// <summary>Creates a new UDS error with the given message.</summary>
    public UdsException(string message)
        : base(CanKitErrorCode.TransportOperationFailed, message) { }

    /// <summary>Creates a new UDS error wrapping an inner cause.</summary>
    public UdsException(string message, Exception innerException)
        : base(CanKitErrorCode.TransportOperationFailed, message, innerException: innerException) { }

    /// <summary>Creates a new UDS error with a specific library error code.</summary>
    protected UdsException(CanKitErrorCode errorCode, string message) : base(errorCode, message) { }
}

/// <summary>
/// Raised when the ECU replies with a Negative Response (0x7F, requestSid, NRC). The three raw
/// bytes are surfaced verbatim so callers can log / branch on any NRC value, not just the ones
/// named in <see cref="UdsNegativeResponseCode"/> (SRS FR-UDS-010).
/// </summary>
public sealed class UdsNegativeResponseException : UdsException
{
    /// <summary>Service the request targeted (the byte returned as byte 2 of the NRC frame).</summary>
    public UdsServiceId RequestedService { get; }

    /// <summary>Raw NRC byte returned by the ECU (byte 3 of the NRC frame).</summary>
    public byte Code { get; }

    /// <summary>
    /// The named NRC value when <see cref="Code"/> matches one of the entries in
    /// <see cref="UdsNegativeResponseCode"/>; <c>null</c> otherwise. Callers should still branch
    /// on <see cref="Code"/> for values not covered by the enum.
    /// </summary>
    public UdsNegativeResponseCode? CodeAsEnum =>
        System.Enum.IsDefined(typeof(UdsNegativeResponseCode), Code)
            ? (UdsNegativeResponseCode)Code
            : null;

    /// <summary>
    /// Human-readable NRC name (e.g. <c>"requestOutOfRange"</c>) when the code is known,
    /// otherwise a hex literal. Handy for log lines.
    /// </summary>
    public string CodeName => CodeAsEnum?.ToString() ?? $"0x{Code:X2}";

    /// <summary>Creates a structured NRC exception.</summary>
    public UdsNegativeResponseException(UdsServiceId requestedService, byte code)
        : base(CanKitErrorCode.ProtocolNegativeResponse,
            $"UDS negative response: service=0x{(byte)requestedService:X2} ({requestedService}), NRC=0x{code:X2} ({(System.Enum.IsDefined(typeof(UdsNegativeResponseCode), code) ? (UdsNegativeResponseCode)code : (object)"unknown")})")
    {
        RequestedService = requestedService;
        Code = code;
    }
}

/// <summary>
/// Raised when the ECU does not deliver a positive or final negative response inside the
/// applicable timing budget:
/// <list type="bullet">
///   <item><description><see cref="UdsTimeoutTimer.P2"/> — the default response window from
///   the request (SRS FR-UDS-008).</description></item>
///   <item><description><see cref="UdsTimeoutTimer.P2Star"/> — the extended window used after
///   the ECU replied with NRC 0x78 (SRS FR-UDS-009).</description></item>
/// </list>
/// </summary>
public sealed class UdsTimeoutException : UdsException
{
    /// <summary>Which timing budget expired.</summary>
    public UdsTimeoutTimer Timer { get; }

    /// <summary>Service that was awaiting a response.</summary>
    public UdsServiceId RequestedService { get; }

    /// <summary>Elapsed budget when the timer expired.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Creates a P2 or P2* timeout exception.</summary>
    public UdsTimeoutException(UdsServiceId requestedService, UdsTimeoutTimer timer, TimeSpan elapsed)
        : base(CanKitErrorCode.ProtocolTimeout,
            $"UDS {(timer == UdsTimeoutTimer.P2 ? "P2" : "P2*")} timeout after {elapsed.TotalMilliseconds:F0} ms waiting for response to service 0x{(byte)requestedService:X2} ({requestedService}).")
    {
        RequestedService = requestedService;
        Timer = timer;
        Elapsed = elapsed;
    }
}

/// <summary>
/// Raised when the ECU response is malformed or does not correlate with the outgoing request
/// (wrong SID echo, positive-response length shorter than the sub-function/DID that was asked
/// for, etc). Distinct from a proper NRC — this class flags protocol violations, not
/// business-layer rejections.
/// </summary>
public sealed class UdsProtocolException : UdsException
{
    /// <summary>Creates a protocol-violation exception.</summary>
    public UdsProtocolException(string message) : base(message) { }
}

/// <summary>
/// Identifies which ISO 14229-1 §7.3 timing budget expired in <see cref="UdsTimeoutException"/>.
/// </summary>
public enum UdsTimeoutTimer
{
    /// <summary>P2_Client — default response timer, restarted by the request.</summary>
    P2,

    /// <summary>P2*_Client — extended response timer, entered after the ECU replied with NRC 0x78.</summary>
    P2Star,
}
