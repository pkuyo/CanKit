using System;

namespace CanKit.Pro.Uds;

/// <summary>
/// Configuration for an <see cref="IUdsClient"/>. All values are captured at construction and
/// treated as immutable for the client's lifetime.
/// </summary>
/// <remarks>
/// <para>Timing terminology follows ISO 14229-1 §7.3:</para>
/// <list type="bullet">
///   <item><description><see cref="P2ClientMax"/> is the default response window a tester waits
///   after transmitting a request before treating the ECU as unresponsive. The specification's
///   default is 50 ms; MVP defaults to 1 s to survive slow simulated ECUs and Windows-CI jitter,
///   real-world callers should override.</description></item>
///   <item><description><see cref="P2StarClientMax"/> is the extended window used after the ECU
///   replied with NRC 0x78 (requestCorrectlyReceived-ResponsePending). Restarted for every
///   further 0x78 (SRS FR-UDS-009). Defaults to 5 s.</description></item>
///   <item><description><see cref="MaxResponsePendingCount"/> bounds how many 0x78 NRCs the
///   client is willing to accept before it gives up. Defaults to 100 (large enough that any
///   healthy ECU will finish first, small enough that a stuck ECU eventually fails loudly).
///   </description></item>
/// </list>
/// </remarks>
public sealed class UdsClientOptions
{
    /// <summary>Default <see cref="P2ClientMax"/> (1 second).</summary>
    public static readonly TimeSpan DefaultP2 = TimeSpan.FromSeconds(1);

    /// <summary>Default <see cref="P2StarClientMax"/> (5 seconds).</summary>
    public static readonly TimeSpan DefaultP2Star = TimeSpan.FromSeconds(5);

    /// <summary>Default <see cref="TesterPresentPeriod"/> (2 seconds; below the ISO 14229 S3
    /// default of 5 seconds).</summary>
    public static readonly TimeSpan DefaultTesterPresentPeriod = TimeSpan.FromSeconds(2);

    /// <summary>
    /// P2_Client_max — maximum time the client waits between sending a request and observing the
    /// first response frame from the ECU (positive, negative, or 0x78 responsePending). Expiring
    /// raises <see cref="UdsTimeoutException"/> with <see cref="UdsTimeoutTimer.P2"/>
    /// (SRS FR-UDS-008).
    /// </summary>
    public TimeSpan P2ClientMax { get; init; } = DefaultP2;

    /// <summary>
    /// P2*_Client_max — maximum time the client waits after receiving NRC 0x78 before treating
    /// the ECU as unresponsive. Restarted for every further 0x78 (SRS FR-UDS-009); once the
    /// budget is exhausted <see cref="UdsTimeoutException"/> with <see cref="UdsTimeoutTimer.P2Star"/>
    /// is raised.
    /// </summary>
    public TimeSpan P2StarClientMax { get; init; } = DefaultP2Star;

    /// <summary>
    /// Upper bound on consecutive NRC 0x78 responses; when the ECU sends more, the client fails
    /// with a <see cref="UdsProtocolException"/> instead of waiting indefinitely. Defaults to
    /// <c>100</c>.
    /// </summary>
    public int MaxResponsePendingCount { get; init; } = 100;

    /// <summary>
    /// Default period for <see cref="IUdsClient.StartTesterPresentKeepAlive"/>. Defaults to 2 s
    /// (below the ISO 14229 default S3 timeout of 5 s so a single missed frame does not drop
    /// the session).
    /// </summary>
    public TimeSpan TesterPresentPeriod { get; init; } = DefaultTesterPresentPeriod;

    /// <summary>
    /// When <c>true</c>, TesterPresent frames sent by the keep-alive helper use the ISO 14229-1
    /// §7.5 "suppressPositiveResponse" bit (sub-function <c>0x80</c>) so the ECU stays silent.
    /// Defaults to <c>true</c> to keep the bus quiet.
    /// </summary>
    public bool KeepAliveSuppressPositiveResponse { get; init; } = true;

    /// <summary>
    /// Convenience clone that returns a new instance with the provided overrides. Useful for
    /// tests that only want to tweak one field of a shared default template.
    /// </summary>
    public UdsClientOptions With(
        TimeSpan? p2ClientMax = null,
        TimeSpan? p2StarClientMax = null,
        int? maxResponsePendingCount = null,
        TimeSpan? testerPresentPeriod = null,
        bool? keepAliveSuppressPositiveResponse = null)
        => new()
        {
            P2ClientMax = p2ClientMax ?? P2ClientMax,
            P2StarClientMax = p2StarClientMax ?? P2StarClientMax,
            MaxResponsePendingCount = maxResponsePendingCount ?? MaxResponsePendingCount,
            TesterPresentPeriod = testerPresentPeriod ?? TesterPresentPeriod,
            KeepAliveSuppressPositiveResponse = keepAliveSuppressPositiveResponse ?? KeepAliveSuppressPositiveResponse,
        };
}
