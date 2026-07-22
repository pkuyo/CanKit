using System;
using CanKit.Core.Exceptions;

namespace CanKit.Pro.CANopen;

/// <summary>
/// Raised when a CANopen frame could not be handed off to the bus (L2 rejection, bus-off, or
/// TX confirmation timeout). Surfaces through the node's
/// <c>BackgroundExceptionOccurred</c> event rather than any single caller — the CANopen wire
/// protocols (SYNC / heartbeat / PDO / EMCY) are fire-and-forget from an application caller's
/// perspective, so there is no single request task to fail. Derives from
/// <see cref="CanKitException"/> so L2/L3/L4 failures can be caught uniformly across the
/// library (NFR-006 error architecture, arc42 ADR-12).
/// </summary>
public sealed class CanOpenTransportException : CanKitException
{
    /// <summary>Constructs a new exception with the given <paramref name="message"/>.</summary>
    public CanOpenTransportException(string message)
        : base(CanKitErrorCode.TransportOperationFailed, message) { }

    /// <summary>Constructs a new exception wrapping <paramref name="inner"/>.</summary>
    public CanOpenTransportException(string message, Exception inner)
        : base(CanKitErrorCode.TransportOperationFailed, message, innerException: inner) { }
}
