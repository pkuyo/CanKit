using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Pro.IsoTp;

namespace CanKit.Tests.TestCases.Uds;

/// <summary>
/// Configurable in-process UDS server used by <see cref="UdsClientTests"/>. It speaks ISO 14229-1
/// over a real <see cref="IIsoTpChannel"/> so the tests exercise the full stack
/// (Virtual bus → RawCan demux → ISO-TP codec → UDS client) rather than a mocked channel.
/// </summary>
/// <remarks>
/// <para>The ECU runs a background loop that pulls PDUs out of its ISO-TP channel and applies
/// per-service handlers registered by the test. Handlers can either return a positive response
/// payload (the ECU replies with <c>[requestSid+0x40, ...handlerBytes]</c>) or throw one of the
/// following sentinel exceptions to trigger UDS-level failure modes:</para>
/// <list type="bullet">
///   <item><description><see cref="EcuNegativeResponse"/> — send a Negative Response with the
///   given NRC byte.</description></item>
///   <item><description><see cref="EcuResponsePending"/> — send N × NRC 0x78, then the "real"
///   response returned by the wrapped delegate.</description></item>
///   <item><description><see cref="EcuSilent"/> — do not reply at all (used to prove the client
///   still respects P2/P2* timeouts).</description></item>
/// </list>
/// </remarks>
public sealed class SimulatedUdsEcu : IDisposable
{
    private readonly IIsoTpChannel _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<byte, Func<byte[], byte[]>> _handlers = new();
    private readonly TaskCompletionSource<object?> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _loop;
    private int _started;
    private int _disposed;

    private int _requestsHandled;
    private byte[]? _lastRequest;

    /// <summary>Number of requests the ECU has answered (for assertions).</summary>
    /// <remarks>
    /// Incremented when a response is <em>committed</em> to the ISO-TP channel (just before
    /// <c>SendAsync</c>), not after TX confirmation. The client can complete as soon as the
    /// frame is on the bus — waiting for SendAsync to return raced net48 CI
    /// (<c>RequestsHandled==0</c> after a successful DiagnosticSessionControl).
    /// </remarks>
    public int RequestsHandled => Volatile.Read(ref _requestsHandled);

    /// <summary>Last request the ECU saw (or <c>null</c>).</summary>
    public byte[]? LastRequest => Volatile.Read(ref _lastRequest);

    /// <summary>Creates an ECU bound to <paramref name="channel"/>. The caller retains channel
    /// ownership.</summary>
    public SimulatedUdsEcu(IIsoTpChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    /// <summary>
    /// Registers or overrides the handler for a UDS service. The handler receives the raw
    /// request bytes (starting with the SID) and returns the positive-response payload
    /// (excluding the response SID which the ECU prepends automatically). Throw one of the
    /// <c>Ecu*</c> sentinel exceptions to trigger UDS-level failure modes.
    /// </summary>
    public SimulatedUdsEcu On(byte serviceId, Func<byte[], byte[]> handler)
    {
        _handlers[serviceId] = handler;
        return this;
    }

    /// <summary>
    /// Starts the ECU's background loop and blocks until it is listening for PDUs. Idempotent.
    /// </summary>
    public SimulatedUdsEcu Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return this;
        _loop = Task.Run(() => RunAsync(_cts.Token));
        // Do not return until the receive loop has entered ReceiveAllAsync; a fixed Sleep
        // still raced net48 CI where the first client request arrived before the enumerator
        // was subscribed.
        if (!_ready.Task.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("SimulatedUdsEcu receive loop failed to become ready.");
        return this;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            // Obtain the enumerator (subscribes the channel read) before signaling ready so
            // BuildPair cannot race a request past an idle Task.Run that has not yet listened.
            var enumerator = _channel.ReceiveAllAsync(ct).GetAsyncEnumerator(ct);
            _ready.TrySetResult(null);
            try
            {
                while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested) break;
                    var pdu = enumerator.Current;
                    if (pdu.Length == 0) continue;

                    Volatile.Write(ref _lastRequest, pdu);
                    byte sid = (byte)(pdu[0] & 0x7F); // Strip suppressPositiveResponse bit for lookup.
                    bool suppressPositive = (pdu[0] == 0x3E) && pdu.Length >= 2 && (pdu[1] & 0x80) != 0;

                    if (!_handlers.TryGetValue(sid, out var handler))
                    {
                        // serviceNotSupported — reply but do not count as a handled request.
                        await SendNrcAsync(sid, 0x11, ct).ConfigureAwait(false);
                        continue;
                    }

                    try
                    {
                        var body = handler(pdu);
                        if (suppressPositive)
                        {
                            Interlocked.Increment(ref _requestsHandled);
                            continue;
                        }

                        var response = new byte[1 + body.Length];
                        response[0] = (byte)(sid + 0x40);
                        if (body.Length > 0) Buffer.BlockCopy(body, 0, response, 1, body.Length);
                        await SendAndCountAsync(response, ct).ConfigureAwait(false);
                    }
                    catch (EcuSilent)
                    {
                        // Explicitly send nothing so we can verify the client's timeout path.
                    }
                    catch (EcuNegativeResponse nrc)
                    {
                        await SendAndCountAsync(new byte[] { 0x7F, sid, nrc.Code }, ct)
                            .ConfigureAwait(false);
                    }
                    catch (EcuResponsePending pending)
                    {
                        for (int i = 0; i < pending.PendingCount && !ct.IsCancellationRequested; i++)
                        {
                            // Pending NRCs are intermediate; only the final response counts.
                            await SendNrcAsync(sid, 0x78, ct).ConfigureAwait(false);
                            if (pending.DelayBetween > TimeSpan.Zero)
                                await Task.Delay(pending.DelayBetween, ct).ConfigureAwait(false);
                        }
                        if (ct.IsCancellationRequested) return;

                        var body = pending.FinalResponse;
                        var response = new byte[1 + body.Length];
                        response[0] = (byte)(sid + 0x40);
                        if (body.Length > 0) Buffer.BlockCopy(body, 0, response, 1, body.Length);
                        await SendAndCountAsync(response, ct).ConfigureAwait(false);
                    }
                    catch (EcuResponsePendingThenSilent pendingSilent)
                    {
                        for (int i = 0; i < pendingSilent.PendingCount && !ct.IsCancellationRequested; i++)
                        {
                            await SendNrcAsync(sid, 0x78, ct).ConfigureAwait(false);
                            if (pendingSilent.DelayBetween > TimeSpan.Zero)
                                await Task.Delay(pendingSilent.DelayBetween, ct).ConfigureAwait(false);
                        }
                        // Then silence: the client's restarted P2* must expire (FR-UDS-008).
                    }
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (ObjectDisposedException) { /* channel disposed */ }
        catch
        {
            // Swallow — the test asserts client-visible behaviour.
        }
        finally
        {
            // If Start() is still waiting and we failed before signaling, unblock it.
            _ready.TrySetResult(null);
        }
    }

    private async Task SendAndCountAsync(byte[] frame, CancellationToken ct)
    {
        // Count before await: virtual-bus peers observe the frame (and complete the client
        // request) while ISO-TP SendAsync may still be waiting on TX confirmation.
        Interlocked.Increment(ref _requestsHandled);
        await _channel.SendAsync(frame, ct).ConfigureAwait(false);
    }

    private Task SendNrcAsync(byte requestSid, byte nrc, CancellationToken ct)
        => _channel.SendAsync(new byte[] { 0x7F, requestSid, nrc }, ct);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _cts.Cancel(); } catch { /* ignored */ }
        try { _loop?.GetAwaiter().GetResult(); } catch { /* ignored */ }
        _cts.Dispose();
    }
}

/// <summary>Sentinel thrown by an ECU handler to force a Negative Response.</summary>
public sealed class EcuNegativeResponse : Exception
{
    /// <summary>NRC byte to send.</summary>
    public byte Code { get; }

    /// <summary>Creates a new NRC sentinel.</summary>
    public EcuNegativeResponse(byte code) : base($"ECU forced NRC 0x{code:X2}")
    {
        Code = code;
    }
}

/// <summary>Sentinel thrown by an ECU handler to force N × NRC 0x78 followed by a real
/// positive-response payload.</summary>
public sealed class EcuResponsePending : Exception
{
    /// <summary>How many NRC 0x78 frames to send before <see cref="FinalResponse"/>.</summary>
    public int PendingCount { get; }

    /// <summary>Optional delay inserted between successive 0x78 frames.</summary>
    public TimeSpan DelayBetween { get; }

    /// <summary>Positive-response payload sent after the pending frames (excluding SID).</summary>
    public byte[] FinalResponse { get; }

    /// <summary>Creates a response-pending sentinel.</summary>
    public EcuResponsePending(int pendingCount, byte[] finalResponse, TimeSpan? delayBetween = null)
        : base($"ECU forced {pendingCount}× NRC 0x78 before finalising.")
    {
        PendingCount = pendingCount;
        FinalResponse = finalResponse;
        DelayBetween = delayBetween ?? TimeSpan.Zero;
    }
}

/// <summary>Sentinel thrown by an ECU handler to force N × NRC 0x78 and then go completely
/// silent — the client's restarted P2* timer must expire (FR-UDS-008).</summary>
public sealed class EcuResponsePendingThenSilent : Exception
{
    /// <summary>How many NRC 0x78 frames to send before going silent.</summary>
    public int PendingCount { get; }

    /// <summary>Optional delay inserted between successive 0x78 frames.</summary>
    public TimeSpan DelayBetween { get; }

    /// <summary>Creates a response-pending-then-silent sentinel.</summary>
    public EcuResponsePendingThenSilent(int pendingCount, TimeSpan? delayBetween = null)
        : base($"ECU forced {pendingCount}× NRC 0x78, then silence.")
    {
        PendingCount = pendingCount;
        DelayBetween = delayBetween ?? TimeSpan.Zero;
    }
}

/// <summary>Sentinel thrown by an ECU handler to skip the response entirely (client should P2
/// timeout).</summary>
public sealed class EcuSilent : Exception
{
    /// <summary>Creates a silent-response sentinel.</summary>
    public EcuSilent() : base("ECU intentionally silent.") { }
}
