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
    /// <see cref="DiagnosticSessionControlAsync(byte, System.Threading.CancellationToken)"/>
    /// and reset to <see cref="UdsSessionType.Default"/> after a successful
    /// <see cref="EcuResetAsync"/>. Starts at <see cref="UdsSessionType.Default"/> because
    /// ISO 14229-1 §9.2 guarantees an ECU starts up in the default session.
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
    /// bytes) echoed back by the ECU. On success, <see cref="CurrentSession"/> is reset to
    /// <see cref="UdsSessionType.Default"/> to match the ECU returning to the default session.
    /// Callers should typically wait for the ECU to reboot before issuing further requests.
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

    /// <summary>
    /// Sends RequestDownload (0x34, ISO 14229-1 §14.2, SRS FR-UDS-012). Negotiates a download
    /// session with the ECU: the tester declares the payload format (compression / encryption
    /// via <paramref name="dataFormatIdentifier"/>) and the target memory range
    /// (<paramref name="memoryAddress"/>, <paramref name="memorySize"/>) whose widths are packed
    /// in <paramref name="addressAndLengthFormatIdentifier"/>. The ECU replies with the maximum
    /// TransferData block length it will accept, wrapped in <see cref="UdsDownloadResponse"/>.
    /// </summary>
    /// <param name="dataFormatIdentifier">Byte encoding the compression method (high nibble)
    /// and encryption method (low nibble); <c>0x00</c> means "no compression / no
    /// encryption".</param>
    /// <param name="addressAndLengthFormatIdentifier">Byte packing the width of
    /// <paramref name="memoryAddress"/> (low nibble) and <paramref name="memorySize"/>
    /// (high nibble) in bytes. Both widths MUST be in <c>1..0x0F</c> and MUST match the
    /// respective buffer lengths.</param>
    /// <param name="memoryAddress">Big-endian target start address.</param>
    /// <param name="memorySize">Big-endian target byte count.</param>
    /// <param name="cancellationToken">Cancels the wait for the ECU response.</param>
    /// <exception cref="ArgumentOutOfRangeException">A width nibble in
    /// <paramref name="addressAndLengthFormatIdentifier"/> is zero, or does not match the
    /// buffer length.</exception>
    Task<UdsDownloadResponse> RequestDownloadAsync(
        byte dataFormatIdentifier,
        byte addressAndLengthFormatIdentifier,
        ReadOnlyMemory<byte> memoryAddress,
        ReadOnlyMemory<byte> memorySize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends RequestUpload (0x35, ISO 14229-1 §14.1, SRS FR-UDS-012). Mirror of
    /// <see cref="RequestDownloadAsync"/> for the tester-reads-from-ECU direction; the returned
    /// <see cref="UdsUploadResponse.MaxNumberOfBlockLength"/> constrains the size of TransferData
    /// responses the ECU will emit.
    /// </summary>
    Task<UdsUploadResponse> RequestUploadAsync(
        byte dataFormatIdentifier,
        byte addressAndLengthFormatIdentifier,
        ReadOnlyMemory<byte> memoryAddress,
        ReadOnlyMemory<byte> memorySize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends TransferData (0x36, ISO 14229-1 §14.3, SRS FR-UDS-012) with the caller-supplied
    /// block sequence counter and payload chunk. Returns the ECU's <c>transferResponseParameterRecord</c>
    /// (the bytes that follow the echoed block sequence counter in the positive response — may
    /// be empty). The client validates that the ECU echoes back the exact
    /// <paramref name="blockSequenceCounter"/>.
    /// </summary>
    /// <param name="blockSequenceCounter">Per ISO 14229-1 §14.3.2 the counter starts at
    /// <c>0x01</c> for the first TransferData following RequestDownload/Upload, is incremented
    /// on each successful transfer, and wraps from <c>0xFF</c> back to <c>0x00</c>.</param>
    /// <param name="data">
    /// Payload chunk. Total request size (SID + BSC + payload = 2 + <c>data.Length</c>) MUST
    /// NOT exceed the ECU's <see cref="UdsDownloadResponse.MaxNumberOfBlockLength"/>. May be
    /// empty when the ECU is expected to synthesise data (e.g. the upload direction).
    /// </param>
    /// <param name="cancellationToken">Cancels the wait for the ECU response.</param>
    Task<byte[]> TransferDataAsync(
        byte blockSequenceCounter,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends RequestTransferExit (0x37, ISO 14229-1 §14.4, SRS FR-UDS-012) to close the current
    /// transfer session. <paramref name="transferRequestParameterRecord"/> is an optional
    /// vendor-specific record (e.g. checksum) appended after the SID.
    /// </summary>
    Task RequestTransferExitAsync(
        ReadOnlyMemory<byte> transferRequestParameterRecord = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience one-shot download that runs the full 0x34 → N × 0x36 → 0x37 sequence for
    /// <paramref name="data"/>. The client negotiates <c>maxNumberOfBlockLength</c> with
    /// RequestDownload, then loops <see cref="TransferDataAsync"/> with an automatically-managed
    /// block sequence counter (starts at <c>0x01</c>, increments per block, wraps
    /// <c>0xFF → 0x00</c>), and finally calls <see cref="RequestTransferExitAsync"/>.
    /// </summary>
    /// <exception cref="UdsProtocolException">The ECU reported a
    /// <c>maxNumberOfBlockLength</c> of <c>0</c> or <c>1</c> so no payload byte would fit in a
    /// TransferData request, or a chunk validation failed.</exception>
    Task DownloadAsync(
        byte dataFormatIdentifier,
        byte addressAndLengthFormatIdentifier,
        ReadOnlyMemory<byte> memoryAddress,
        ReadOnlyMemory<byte> memorySize,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);
}
