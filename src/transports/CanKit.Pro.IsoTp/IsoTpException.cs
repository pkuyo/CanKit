using System;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// Base type for ISO-TP protocol errors surfaced by <see cref="IIsoTpChannel"/>.
/// </summary>
public class IsoTpException : Exception
{
    /// <summary>Creates a new <see cref="IsoTpException"/>.</summary>
    public IsoTpException(string message) : base(message) { }

    /// <summary>Creates a new <see cref="IsoTpException"/> with an inner exception.</summary>
    public IsoTpException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Raised when an ISO-TP timeout (N_As, N_Bs or N_Cr) expires on a still-active PDU
/// (SRS FR-TP-010).
/// </summary>
public sealed class IsoTpTimeoutException : IsoTpException
{
    /// <summary>Which ISO 15765-2 timer expired.</summary>
    public IsoTpTimer Timer { get; }

    /// <summary>Creates a timeout exception naming which timer expired.</summary>
    public IsoTpTimeoutException(IsoTpTimer timer, string message) : base(message)
    {
        Timer = timer;
    }
}

/// <summary>
/// Raised when the peer sends <see cref="FlowStatus.Overflow"/> in response to a First Frame
/// (SRS FR-TP-012).
/// </summary>
public sealed class IsoTpOverflowException : IsoTpException
{
    /// <summary>Creates an overflow exception.</summary>
    public IsoTpOverflowException(string message) : base(message) { }
}

/// <summary>
/// Raised when the sender receives more than <see cref="IsoTpChannelOptions.WftMax"/>
/// consecutive <see cref="FlowStatus.Wait"/> Flow-Control frames (SRS FR-TP-011).
/// </summary>
public sealed class IsoTpWaitFrameLimitExceededException : IsoTpException
{
    /// <summary>Number of Wait frames received before the limit was hit.</summary>
    public int WaitFramesReceived { get; }

    /// <summary>Configured WFTmax.</summary>
    public int Limit { get; }

    /// <summary>Creates a WFTmax exception.</summary>
    public IsoTpWaitFrameLimitExceededException(int received, int limit)
        : base($"Peer sent {received} consecutive Flow-Control Wait frames, exceeding WFTmax={limit}.")
    {
        WaitFramesReceived = received;
        Limit = limit;
    }
}

/// <summary>
/// Raised when the driver rejected a frame the channel tried to transmit
/// (<see cref="RawCan.TxConfirmFailureReason.Rejected"/>).
/// </summary>
public sealed class IsoTpSendRejectedException : IsoTpException
{
    /// <summary>Creates a send-rejected exception.</summary>
    public IsoTpSendRejectedException(string message) : base(message) { }
}

/// <summary>
/// ISO 15765-2 network-layer timers observed by <see cref="IsoTpTimeoutException"/>.
/// </summary>
public enum IsoTpTimer
{
    /// <summary>N_As — sender's TX-confirmation timer (SF/FF/CF -> driver acknowledgment).</summary>
    NAs,

    /// <summary>N_Bs — sender's Flow-Control-wait timer (last CF-of-block -> next FC).</summary>
    NBs,

    /// <summary>N_Cr — receiver's Consecutive-Frame timer (CF -> next CF).</summary>
    NCr,
}
