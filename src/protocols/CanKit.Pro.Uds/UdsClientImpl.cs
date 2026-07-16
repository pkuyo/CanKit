using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Pro.IsoTp;

namespace CanKit.Pro.Uds;

/// <summary>
/// Default <see cref="IUdsClient"/> implementation. Owns a single request lock so at most one
/// UDS request is on the wire at any time (ISO 14229-1 §7.3), plus the P2/P2* wait loop that
/// implements NRC 0x78 handling.
/// </summary>
/// <remarks>
/// <para>
/// The client makes no assumptions about how the underlying <see cref="IIsoTpChannel"/> handles
/// concurrency — every write is a single <see cref="IIsoTpChannel.SendAsync"/> and every read
/// is a single <see cref="IIsoTpChannel.ReceiveAsync"/>. Because we hold the request lock
/// across the send + wait, we know the next reassembled PDU on the channel belongs to the
/// current request (the ECU only speaks in response to a request, ISO 14229-1 §7.3).
/// Multi-step services such as SecurityAccess keep that same lock across both on-the-wire
/// exchanges so keep-alive traffic cannot interleave.
/// </para>
/// <para>
/// Before each send (and on abort paths) the client calls
/// <see cref="IIsoTpChannel.DiscardPendingPdus"/> so a late ECU reply from a cancelled or
/// timed-out wait cannot be consumed as the answer to a later request. SID correlation during
/// the wait loop remains as a second line of defense for stray frames that arrive while a
/// request is still outstanding.
/// </para>
/// </remarks>
internal sealed class UdsClientImpl : IUdsClient
{
    private const byte NegativeResponseSid = 0x7F;
    private const byte PositiveResponseOffset = 0x40;
    private const byte NrcResponsePending = 0x78;
    private const byte SuppressPositiveResponseBit = 0x80;

    private readonly IIsoTpChannel _channel;
    private readonly bool _ownsChannel;
    private readonly UdsClientOptions _options;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();

    private byte _currentSession = (byte)UdsSessionType.Default;
    private TesterPresentKeepAlive? _keepAlive;
    private int _disposed;

    public UdsClientImpl(IIsoTpChannel channel, UdsClientOptions options, bool ownsChannel)
    {
        _channel = channel;
        _options = options;
        _ownsChannel = ownsChannel;

        if (options.P2ClientMax <= TimeSpan.Zero)
            throw new ArgumentException("P2ClientMax must be positive.", nameof(options));
        if (options.P2StarClientMax <= TimeSpan.Zero)
            throw new ArgumentException("P2StarClientMax must be positive.", nameof(options));
        if (options.MaxResponsePendingCount < 0)
            throw new ArgumentException("MaxResponsePendingCount must be non-negative.", nameof(options));
        if (options.TesterPresentPeriod <= TimeSpan.Zero)
            throw new ArgumentException("TesterPresentPeriod must be positive.", nameof(options));
    }

    public IIsoTpChannel Channel => _channel;
    public UdsClientOptions Options => _options;
    public byte CurrentSession => Volatile.Read(ref _currentSession);

    // ---------------------------------------------------------------------------------------
    // Public service methods (see IUdsClient for XML docs).
    // ---------------------------------------------------------------------------------------

    public async Task<byte[]> DiagnosticSessionControlAsync(UdsSessionType session,
        CancellationToken cancellationToken = default)
        => await DiagnosticSessionControlAsync((byte)session, cancellationToken).ConfigureAwait(false);

    public async Task<byte[]> DiagnosticSessionControlAsync(byte sessionType,
        CancellationToken cancellationToken = default)
    {
        byte sub = (byte)(sessionType & 0x7F);
        var request = new byte[] { (byte)UdsServiceId.DiagnosticSessionControl, sub };
        var response = await ExecuteAsync(UdsServiceId.DiagnosticSessionControl, request,
            cancellationToken).ConfigureAwait(false);

        // Positive response layout (ISO 14229-1 §9.2.2.4):
        //   [0]=0x50 [1]=sessionType [2..5]=sessionParameterRecord (P2_server/P2*_server timing)
        // Some ECUs omit the parameter record on legacy sessions; we accept >= 2 bytes.
        if (response.Length < 2 || response[1] != sub)
            throw new UdsProtocolException(
                $"DiagnosticSessionControl response sub-function mismatch (expected 0x{sub:X2}, got payload length {response.Length}).");

        Volatile.Write(ref _currentSession, sub);
        int recordLength = response.Length - 2;
        var record = new byte[recordLength];
        if (recordLength > 0) Buffer.BlockCopy(response, 2, record, 0, recordLength);
        return record;
    }

    public async Task<byte[]> EcuResetAsync(UdsEcuResetType resetType,
        CancellationToken cancellationToken = default)
    {
        var request = new byte[] { (byte)UdsServiceId.EcuReset, (byte)resetType };
        var response = await ExecuteAsync(UdsServiceId.EcuReset, request,
            cancellationToken).ConfigureAwait(false);

        // Positive response layout: [0]=0x51 [1]=resetType [2]?=powerDownTime.
        if (response.Length < 2 || response[1] != (byte)resetType)
            throw new UdsProtocolException(
                $"ECUReset response reset-type mismatch (expected 0x{(byte)resetType:X2}, got payload length {response.Length}).");
        int tail = response.Length - 2;
        var record = new byte[tail];
        if (tail > 0) Buffer.BlockCopy(response, 2, record, 0, tail);
        return record;
    }

    public async Task<byte[]> ReadDataByIdentifierAsync(ushort dataIdentifier,
        CancellationToken cancellationToken = default)
    {
        var request = new byte[]
        {
            (byte)UdsServiceId.ReadDataByIdentifier,
            (byte)(dataIdentifier >> 8),
            (byte)(dataIdentifier & 0xFF),
        };
        var response = await ExecuteAsync(UdsServiceId.ReadDataByIdentifier, request,
            cancellationToken).ConfigureAwait(false);

        // Positive response: [0]=0x62 [1..2]=DID [3..]=dataRecord.
        if (response.Length < 3)
            throw new UdsProtocolException(
                $"ReadDataByIdentifier response too short ({response.Length} bytes).");
        ushort echoed = (ushort)((response[1] << 8) | response[2]);
        if (echoed != dataIdentifier)
            throw new UdsProtocolException(
                $"ReadDataByIdentifier response DID mismatch (requested 0x{dataIdentifier:X4}, got 0x{echoed:X4}).");
        int len = response.Length - 3;
        var data = new byte[len];
        if (len > 0) Buffer.BlockCopy(response, 3, data, 0, len);
        return data;
    }

    public async Task<IReadOnlyDictionary<ushort, byte[]>> ReadDataByIdentifierAsync(
        IReadOnlyList<ushort> dataIdentifiers, CancellationToken cancellationToken = default)
    {
        if (dataIdentifiers is null) throw new ArgumentNullException(nameof(dataIdentifiers));
        if (dataIdentifiers.Count == 0)
            throw new ArgumentException("At least one DID is required.", nameof(dataIdentifiers));

        var request = new byte[1 + dataIdentifiers.Count * 2];
        request[0] = (byte)UdsServiceId.ReadDataByIdentifier;
        for (int i = 0; i < dataIdentifiers.Count; i++)
        {
            request[1 + i * 2] = (byte)(dataIdentifiers[i] >> 8);
            request[2 + i * 2] = (byte)(dataIdentifiers[i] & 0xFF);
        }

        var response = await ExecuteAsync(UdsServiceId.ReadDataByIdentifier, request,
            cancellationToken).ConfigureAwait(false);

        // Multi-DID positive response (ISO 14229-1 §9.3.4.4): [0]=0x62 then
        // (DID[2 bytes] + dataRecord[len bytes])* for every returned DID. Because record lengths
        // are implicit, we walk the response by matching each two-byte DID against the request
        // and treating everything up to the next known DID (or end-of-buffer) as its dataRecord.
        var requested = new HashSet<ushort>();
        foreach (var did in dataIdentifiers) requested.Add(did);

        var result = new Dictionary<ushort, byte[]>(dataIdentifiers.Count);
        int cursor = 1;
        int? currentDidStart = null;
        ushort currentDid = 0;

        while (cursor <= response.Length)
        {
            bool atEnd = cursor + 2 > response.Length;
            ushort maybeDid = atEnd
                ? (ushort)0
                : (ushort)((response[cursor] << 8) | response[cursor + 1]);

            if (!atEnd && requested.Contains(maybeDid) && !result.ContainsKey(maybeDid))
            {
                if (currentDidStart is int prev)
                {
                    int recLen = cursor - prev;
                    if (recLen <= 0)
                        throw new UdsProtocolException(
                            $"Multi-DID ReadDataByIdentifier response: zero-length record for DID 0x{currentDid:X4}.");
                    var rec = new byte[recLen];
                    Buffer.BlockCopy(response, prev, rec, 0, recLen);
                    result[currentDid] = rec;
                }
                currentDid = maybeDid;
                currentDidStart = cursor + 2;
                cursor += 2;
            }
            else if (atEnd)
            {
                if (currentDidStart is int prev)
                {
                    int recLen = response.Length - prev;
                    if (recLen <= 0)
                        throw new UdsProtocolException(
                            $"Multi-DID ReadDataByIdentifier response: zero-length record for DID 0x{currentDid:X4}.");
                    var rec = new byte[recLen];
                    Buffer.BlockCopy(response, prev, rec, 0, recLen);
                    result[currentDid] = rec;
                }
                break;
            }
            else
            {
                cursor++;
            }
        }

        if (result.Count != dataIdentifiers.Count)
        {
            var missing = new List<string>();
            foreach (var did in dataIdentifiers)
                if (!result.ContainsKey(did)) missing.Add($"0x{did:X4}");
            throw new UdsProtocolException(
                $"Multi-DID ReadDataByIdentifier response missing DIDs: {string.Join(", ", missing)}.");
        }

        return result;
    }

    public async Task WriteDataByIdentifierAsync(ushort dataIdentifier, ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        var request = new byte[3 + data.Length];
        request[0] = (byte)UdsServiceId.WriteDataByIdentifier;
        request[1] = (byte)(dataIdentifier >> 8);
        request[2] = (byte)(dataIdentifier & 0xFF);
        if (data.Length > 0) data.Span.CopyTo(request.AsSpan(3));

        var response = await ExecuteAsync(UdsServiceId.WriteDataByIdentifier, request,
            cancellationToken).ConfigureAwait(false);

        // Positive response: [0]=0x6E [1..2]=DID.
        if (response.Length < 3)
            throw new UdsProtocolException(
                $"WriteDataByIdentifier response too short ({response.Length} bytes).");
        ushort echoed = (ushort)((response[1] << 8) | response[2]);
        if (echoed != dataIdentifier)
            throw new UdsProtocolException(
                $"WriteDataByIdentifier response DID mismatch (wrote 0x{dataIdentifier:X4}, got 0x{echoed:X4}).");
    }

    public async Task<byte[]> RoutineControlAsync(UdsRoutineControlType routineType,
        ushort routineIdentifier, ReadOnlyMemory<byte> routineControlOptionRecord = default,
        CancellationToken cancellationToken = default)
    {
        var request = new byte[4 + routineControlOptionRecord.Length];
        request[0] = (byte)UdsServiceId.RoutineControl;
        request[1] = (byte)routineType;
        request[2] = (byte)(routineIdentifier >> 8);
        request[3] = (byte)(routineIdentifier & 0xFF);
        if (routineControlOptionRecord.Length > 0)
            routineControlOptionRecord.Span.CopyTo(request.AsSpan(4));

        var response = await ExecuteAsync(UdsServiceId.RoutineControl, request,
            cancellationToken).ConfigureAwait(false);

        // Positive response: [0]=0x71 [1]=sub [2..3]=routineId [4..]=routineInfo+statusRecord.
        if (response.Length < 4 || response[1] != (byte)routineType)
            throw new UdsProtocolException(
                $"RoutineControl response sub-function mismatch (expected 0x{(byte)routineType:X2}).");
        ushort echoed = (ushort)((response[2] << 8) | response[3]);
        if (echoed != routineIdentifier)
            throw new UdsProtocolException(
                $"RoutineControl response routineId mismatch (requested 0x{routineIdentifier:X4}, got 0x{echoed:X4}).");
        int tail = response.Length - 4;
        var info = new byte[tail];
        if (tail > 0) Buffer.BlockCopy(response, 4, info, 0, tail);
        return info;
    }

    public async Task SecurityAccessAsync(byte requestSeedLevel, Func<byte[], byte[]> computeKey,
        CancellationToken cancellationToken = default)
    {
        if (computeKey is null) throw new ArgumentNullException(nameof(computeKey));
        if (requestSeedLevel == 0 || requestSeedLevel >= 0x7F || (requestSeedLevel & 0x01) == 0)
            throw new ArgumentOutOfRangeException(nameof(requestSeedLevel),
                "SecurityAccess requestSeedLevel must be an odd byte in 0x01..0x7F.");

        byte sendKeyLevel = (byte)(requestSeedLevel + 1);

        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeCts.Token);
        var linkedToken = linked.Token;

        // Hold the request lock across seed + sendKey so TesterPresent keep-alive (or another
        // UDS call) cannot interleave and break the ISO 14229-1 security-access sequence
        // (NRC requestSequenceError on real ECUs).
        await _requestLock.WaitAsync(linkedToken).ConfigureAwait(false);
        try
        {
            var seedRequest = new byte[] { (byte)UdsServiceId.SecurityAccess, requestSeedLevel };
            var seedResponse = await ExecuteCoreAsync(UdsServiceId.SecurityAccess, seedRequest,
                linkedToken).ConfigureAwait(false);

            // Positive response: [0]=0x67 [1]=requestSeedLevel [2..]=seed. A zero-length seed means
            // "already unlocked" per ISO 14229-1 §9.4.5.3 — the client MUST NOT send the key.
            if (seedResponse.Length < 2 || seedResponse[1] != requestSeedLevel)
                throw new UdsProtocolException(
                    $"SecurityAccess seed response sub-function mismatch (expected 0x{requestSeedLevel:X2}).");
            int seedLen = seedResponse.Length - 2;
            if (seedLen == 0) return;

            var seed = new byte[seedLen];
            Buffer.BlockCopy(seedResponse, 2, seed, 0, seedLen);
            byte[] key = computeKey(seed)
                ?? throw new UdsProtocolException("SecurityAccess computeKey callback returned null.");
            if (key.Length == 0)
                throw new UdsProtocolException("SecurityAccess computeKey callback returned an empty key.");

            var keyRequest = new byte[2 + key.Length];
            keyRequest[0] = (byte)UdsServiceId.SecurityAccess;
            keyRequest[1] = sendKeyLevel;
            Buffer.BlockCopy(key, 0, keyRequest, 2, key.Length);

            var keyResponse = await ExecuteCoreAsync(UdsServiceId.SecurityAccess, keyRequest,
                linkedToken).ConfigureAwait(false);
            if (keyResponse.Length < 2 || keyResponse[1] != sendKeyLevel)
                throw new UdsProtocolException(
                    $"SecurityAccess sendKey response sub-function mismatch (expected 0x{sendKeyLevel:X2}).");
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async Task TesterPresentAsync(bool suppressPositiveResponse = true,
        CancellationToken cancellationToken = default)
    {
        byte sub = suppressPositiveResponse ? SuppressPositiveResponseBit : (byte)0x00;
        var request = new byte[] { (byte)UdsServiceId.TesterPresent, sub };

        if (suppressPositiveResponse)
        {
            // Fire-and-forget: acquire the request lock so we don't interleave with a real
            // request, send the frame, then release. No response is expected.
            ThrowIfDisposed();
            await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _channel.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _requestLock.Release();
            }
            return;
        }

        var response = await ExecuteAsync(UdsServiceId.TesterPresent, request,
            cancellationToken).ConfigureAwait(false);
        if (response.Length < 2 || response[1] != 0x00)
            throw new UdsProtocolException(
                $"TesterPresent response sub-function mismatch (expected 0x00, got payload length {response.Length}).");
    }

    public IDisposable StartTesterPresentKeepAlive(TimeSpan? period = null)
    {
        ThrowIfDisposed();
        var interval = period ?? _options.TesterPresentPeriod;
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(period), "TesterPresent period must be positive.");

        var candidate = new TesterPresentKeepAlive(this, interval,
            _options.KeepAliveSuppressPositiveResponse);
        if (Interlocked.CompareExchange(ref _keepAlive, candidate, null) is not null)
        {
            candidate.Dispose();
            throw new InvalidOperationException(
                "A TesterPresent keep-alive is already running; dispose it before starting another.");
        }
        candidate.Start();
        return candidate;
    }

    public async Task<byte[]> SendRawAsync(ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken = default)
    {
        if (request.Length == 0)
            throw new ArgumentException("Request must contain at least a SID byte.", nameof(request));
        var sid = (UdsServiceId)request.Span[0];
        var copy = new byte[request.Length];
        request.Span.CopyTo(copy);
        return await ExecuteAsync(sid, copy, cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------------------
    // Shared request/response engine (P2/P2* + NRC 0x78 loop + structured NRC surfacing).
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Sends <paramref name="request"/>, waits for the first response inside P2, then keeps
    /// waiting inside P2* for as long as the ECU replies with NRC 0x78. Validates that the
    /// positive response SID matches (request SID + 0x40) and unpacks structured NRCs.
    /// </summary>
    private async Task<byte[]> ExecuteAsync(UdsServiceId serviceId, byte[] request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeCts.Token);
        var linkedToken = linked.Token;

        await _requestLock.WaitAsync(linkedToken).ConfigureAwait(false);
        try
        {
            return await ExecuteCoreAsync(serviceId, request, linkedToken).ConfigureAwait(false);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    /// <summary>
    /// Request/response engine that assumes <see cref="_requestLock"/> is already held.
    /// Used by <see cref="ExecuteAsync"/> and by multi-step services (SecurityAccess) that must
    /// keep the lock across more than one on-the-wire exchange.
    /// </summary>
    private async Task<byte[]> ExecuteCoreAsync(UdsServiceId serviceId, byte[] request,
        CancellationToken linkedToken)
    {
        // Drop any late reply left over from a previous aborted/timed-out wait before we put a
        // new request on the wire. SID correlation alone is insufficient when the next request
        // uses the same service (the stale positive response SID would match).
        DiscardStalePdus();

        await _channel.SendAsync(request, linkedToken).ConfigureAwait(false);

        var timer = Stopwatch.StartNew();
        var timeout = _options.P2ClientMax;
        var timerKind = UdsTimeoutTimer.P2;
        int pendingCount = 0;

        try
        {
            while (true)
            {
                byte[] response = await ReceiveWithTimeoutAsync(
                    serviceId, timerKind, timeout, timer.Elapsed, linkedToken).ConfigureAwait(false);

                if (response.Length == 0)
                    throw new UdsProtocolException(
                        $"Empty UDS response received for service 0x{(byte)serviceId:X2}.");

                // Negative response layout: [0]=0x7F [1]=requestSid [2]=NRC.
                if (response[0] == NegativeResponseSid)
                {
                    if (response.Length < 3)
                        throw new UdsProtocolException(
                            $"Malformed negative response (length={response.Length}).");
                    var echoed = (UdsServiceId)response[1];
                    if (echoed != serviceId)
                    {
                        // Stray NRC for a different SID — treat as background noise and keep
                        // waiting inside the same budget.
                        continue;
                    }

                    byte nrc = response[2];
                    if (nrc == NrcResponsePending)
                    {
                        pendingCount++;
                        if (pendingCount > _options.MaxResponsePendingCount)
                            throw new UdsProtocolException(
                                $"ECU sent {pendingCount} consecutive NRC 0x78 responses, exceeding MaxResponsePendingCount={_options.MaxResponsePendingCount}.");

                        // Restart the wait budget on P2* (SRS FR-UDS-009, ISO 14229-1 §7.3.3).
                        timer.Restart();
                        timeout = _options.P2StarClientMax;
                        timerKind = UdsTimeoutTimer.P2Star;
                        continue;
                    }

                    throw new UdsNegativeResponseException(serviceId, nrc);
                }

                byte expectedPositiveSid = (byte)((byte)serviceId + PositiveResponseOffset);
                if (response[0] != expectedPositiveSid)
                {
                    // Stray positive response for a different request; discard and keep waiting.
                    continue;
                }

                return response;
            }
        }
        catch
        {
            // Best-effort: if a PDU is already sitting in the inbox when we abort (e.g. cancel
            // raced with arrival), drop it under the lock so it cannot poison the next caller.
            DiscardStalePdus();
            throw;
        }
    }

    private void DiscardStalePdus()
    {
        try
        {
            _channel.DiscardPendingPdus();
        }
        catch (ObjectDisposedException)
        {
            // Channel is going away; nothing left to drain.
        }
    }

    /// <summary>
    /// Waits on <see cref="IIsoTpChannel.ReceiveAsync"/> with the currently applicable
    /// (P2 or P2*) timeout, taking already-elapsed time into account so a single wait budget
    /// isn't re-set to full when the loop iterates for a stray frame.
    /// </summary>
    private async Task<byte[]> ReceiveWithTimeoutAsync(UdsServiceId serviceId,
        UdsTimeoutTimer timerKind, TimeSpan budget, TimeSpan elapsedInBudget,
        CancellationToken linkedToken)
    {
        var remaining = budget - elapsedInBudget;
        if (remaining <= TimeSpan.Zero)
            throw new UdsTimeoutException(serviceId, timerKind, elapsedInBudget);

        using var timeoutCts = new CancellationTokenSource(remaining);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(
            linkedToken, timeoutCts.Token);

        try
        {
            return await _channel.ReceiveAsync(combined.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
                                                 && !linkedToken.IsCancellationRequested)
        {
            throw new UdsTimeoutException(serviceId, timerKind, budget);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(UdsClient));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Interlocked.Exchange(ref _keepAlive, null)?.Dispose();

        try { _lifetimeCts.Cancel(); } catch { /* already disposed */ }
        _lifetimeCts.Dispose();
        _requestLock.Dispose();

        if (_ownsChannel)
        {
            try { _channel.Dispose(); } catch { /* Dispose should not throw */ }
        }
    }

    // ---------------------------------------------------------------------------------------
    // TesterPresent keep-alive helper.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Background timer that periodically calls <see cref="TesterPresentAsync"/> until disposed.
    /// Uses <see cref="Task.Delay(System.TimeSpan, System.Threading.CancellationToken)"/> rather
    /// than a <see cref="System.Threading.Timer"/> to keep the state machine linear and share
    /// the client's request lock naturally.
    /// </summary>
    private sealed class TesterPresentKeepAlive : IDisposable
    {
        private readonly UdsClientImpl _owner;
        private readonly TimeSpan _period;
        private readonly bool _suppress;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;
        private int _disposed;

        public TesterPresentKeepAlive(UdsClientImpl owner, TimeSpan period, bool suppress)
        {
            _owner = owner;
            _period = period;
            _suppress = suppress;
        }

        public void Start()
        {
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_period, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { return; }

                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        await _owner.TesterPresentAsync(_suppress, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (ObjectDisposedException) { return; }
                    catch
                    {
                        // Swallow individual failures so a transient hiccup doesn't tear down
                        // the whole keep-alive; the next tick tries again. Callers who need to
                        // observe keep-alive failures can subscribe to the underlying channel's
                        // BackgroundExceptionOccurred instead.
                    }
                }
            }
            finally
            {
                Interlocked.CompareExchange(ref _owner._keepAlive, null, this);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _cts.Cancel(); } catch { /* ignored */ }
            try { _loop?.GetAwaiter().GetResult(); } catch { /* ignored */ }
            _cts.Dispose();
        }
    }
}
