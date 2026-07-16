using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CanKit.Pro.Uds;

/// <summary>
/// A single Unified Diagnostic Services (ISO 14229-1) client bound to one
/// <see cref="IsoTp.IIsoTpChannel"/>. One client = one tester ↔ ECU relationship; overlapping
/// requests from multiple callers are serialized by the client so at most one UDS request is
/// outstanding at any time (mirrors ISO 14229-1 §7.3's "one active request per tester" model).
/// </summary>
/// <remarks>
/// <para>
/// The MVP surface covers the seven services listed in SRS FR-UDS-001..007 (0x10, 0x11, 0x22,
/// 0x27, 0x2E, 0x31, 0x3E) plus the raw escape hatch
/// <see cref="SendRawAsync(ReadOnlyMemory{byte}, System.Threading.CancellationToken)"/>. Every
/// method:
/// </para>
/// <list type="bullet">
///   <item><description>Serializes on the client's internal request lock — callers may invoke
///   from arbitrary threads.</description></item>
///   <item><description>Applies P2/P2* timing (SRS FR-UDS-008/009): the initial response is
///   awaited for at most <see cref="UdsClientOptions.P2ClientMax"/>; every NRC 0x78
///   (requestCorrectlyReceived-ResponsePending) restarts the P2*
///   (<see cref="UdsClientOptions.P2StarClientMax"/>) timer.</description></item>
///   <item><description>Surfaces negative responses as <see cref="UdsNegativeResponseException"/>
///   with the request SID and the raw NRC byte (SRS FR-UDS-010).</description></item>
/// </list>
/// <para>
/// The client honours the caller's <see cref="System.Threading.CancellationToken"/> for both the
/// serialization gate and the response wait; a cancelled call abandons the response but does
/// not corrupt the client (the next call will still see a clean state).
/// </para>
/// <para><see cref="IDisposable.Dispose"/> is thread-safe and idempotent; disposing an active
/// client cancels any pending request and stops the TesterPresent keep-alive (if any).</para>
/// </remarks>
public interface IUdsClient : IDisposable
{
    /// <summary>The channel this client is bound to (never <c>null</c>).</summary>
    IsoTp.IIsoTpChannel Channel { get; }

    /// <summary>The immutable options used at construction.</summary>
    UdsClientOptions Options { get; }

    /// <summary>
    /// The most recently negotiated session, updated on a successful
    /// <see cref="DiagnosticSessionControlAsync(byte, System.Threading.CancellationToken)"/>.
    /// Starts at <see cref="UdsSessionType.Default"/> because ISO 14229-1 §9.2 guarantees an ECU
    /// starts up in the default session.
    /// </summary>
    byte CurrentSession { get; }

    /// <summary>
    /// Sends DiagnosticSessionControl (0x10, SRS FR-UDS-001) with a named
    /// <see cref="UdsSessionType"/>. Returns the raw parameter bytes echoed back by the ECU
    /// (session parameter record, typically 4 bytes carrying P2/P2* server timing).
    /// </summary>
    Task<byte[]> DiagnosticSessionControlAsync(UdsSessionType session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends DiagnosticSessionControl (0x10) with a raw sub-function byte. Convenience for
    /// vendor-specific session numbers that are not covered by <see cref="UdsSessionType"/>.
    /// </summary>
    Task<byte[]> DiagnosticSessionControlAsync(byte sessionType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends ECUReset (0x11, SRS FR-UDS-005) and returns the raw powerDownTime parameter (0..1
    /// bytes) echoed back by the ECU. Callers should typically wait for the ECU to reboot before
    /// issuing further requests.
    /// </summary>
    Task<byte[]> EcuResetAsync(UdsEcuResetType resetType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends ReadDataByIdentifier (0x22, SRS FR-UDS-002) for a single Data Identifier. Returns
    /// the raw data-record bytes that followed the DID in the positive response.
    /// </summary>
    Task<byte[]> ReadDataByIdentifierAsync(ushort dataIdentifier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends ReadDataByIdentifier (0x22) with more than one DID in a single request
    /// (SRS FR-UDS-011, ISO 14229-1 §9.3.4). Returns a dictionary keyed by DID with each
    /// requested identifier's raw data-record bytes.
    /// </summary>
    /// <param name="dataIdentifiers">DIDs to request, in the order they are placed on the wire.</param>
    /// <param name="dataRecordLengths">
    /// Expected <c>dataRecord</c> length (bytes) for every requested DID. ISO 14229-1 §9.3.4.4
    /// does not encode lengths in the positive response — the client must know them from the
    /// ECU's DID definition (ODX/CDD/etc.). Zero-length records are allowed.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait for the ECU response.</param>
    /// <exception cref="ArgumentException">A requested DID has no length entry, or a length is
    /// negative.</exception>
    /// <exception cref="UdsProtocolException">The ECU response is malformed, truncated, missing a
    /// DID, or contains DIDs that were not requested.</exception>
    Task<IReadOnlyDictionary<ushort, byte[]>> ReadDataByIdentifierAsync(
        IReadOnlyList<ushort> dataIdentifiers,
        IReadOnlyDictionary<ushort, int> dataRecordLengths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends WriteDataByIdentifier (0x2E, SRS FR-UDS-003). Completes successfully when the ECU
    /// echoes the DID; the DID echo is validated against the request.
    /// </summary>
    Task WriteDataByIdentifierAsync(ushort dataIdentifier, ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends RoutineControl (0x31, SRS FR-UDS-004). The routine control option record
    /// (<paramref name="routineControlOptionRecord"/>) is sent verbatim after the routine
    /// identifier; the returned array contains the routine info bytes from the positive
    /// response (excluding the echoed sub-function and routine identifier).
    /// </summary>
    Task<byte[]> RoutineControlAsync(UdsRoutineControlType routineType, ushort routineIdentifier,
        ReadOnlyMemory<byte> routineControlOptionRecord = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the ISO 14229-1 §9.4 SecurityAccess (0x27, SRS FR-UDS-006) two-step exchange:
    /// <list type="number">
    ///   <item><description>Send <c>0x27 requestSeedLevel</c>, receive the seed bytes.</description></item>
    ///   <item><description>Invoke <paramref name="computeKey"/> synchronously with the seed and
    ///   the returned key, then send <c>0x27 sendKeyLevel</c> with the caller-computed key
    ///   bytes.</description></item>
    /// </list>
    /// <paramref name="requestSeedLevel"/> must be an odd byte in <c>0x01..0x7F</c> and
    /// <c>sendKeyLevel = requestSeedLevel + 1</c> is derived automatically. A zero-length seed
    /// (ISO 14229-1 §9.4.5.3 "already unlocked") short-circuits the exchange without invoking
    /// <paramref name="computeKey"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="requestSeedLevel"/> is not
    /// an odd byte in the range <c>0x01..0x7F</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="computeKey"/> is <c>null</c>.</exception>
    Task SecurityAccessAsync(byte requestSeedLevel, Func<byte[], byte[]> computeKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a single TesterPresent (0x3E, SRS FR-UDS-007). When
    /// <paramref name="suppressPositiveResponse"/> is <c>true</c> the ISO 14229-1 §7.5
    /// suppressPositiveResponse bit is set (sub-function <c>0x80</c>) and the method returns as
    /// soon as the request is on the wire — the ECU is expected to stay silent. Otherwise the
    /// method awaits the positive response.
    /// </summary>
    Task TesterPresentAsync(bool suppressPositiveResponse = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a background TesterPresent (0x3E) keep-alive that fires every
    /// <paramref name="period"/> (defaults to <see cref="UdsClientOptions.TesterPresentPeriod"/>)
    /// until the returned <see cref="IDisposable"/> is disposed or the client itself is
    /// disposed. The keep-alive uses <see cref="TesterPresentAsync(bool,
    /// System.Threading.CancellationToken)"/> under the hood so it serializes with normal
    /// requests through the same request lock; a slow request never causes a stale keep-alive
    /// frame to interleave (SRS FR-UDS-007).
    /// </summary>
    /// <param name="period">Override for the send interval; must be positive.</param>
    /// <exception cref="InvalidOperationException">A keep-alive is already running; call
    /// <see cref="IDisposable.Dispose"/> on the previous handle first.</exception>
    IDisposable StartTesterPresentKeepAlive(TimeSpan? period = null);

    /// <summary>
    /// Sends the raw <paramref name="request"/> bytes verbatim (starting with the SID) and
    /// returns the ECU's raw positive-response bytes (again including the response SID). The
    /// same P2/P2* timing and NRC handling apply as for the strongly-typed methods.
    /// </summary>
    Task<byte[]> SendRawAsync(ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken = default);
}
