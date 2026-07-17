using System;

namespace CanKit.Pro.J1939;

/// <summary>Base class for exceptions thrown by <see cref="IJ1939Node"/> operations.</summary>
public class J1939NodeException : Exception
{
    /// <summary>Constructs an exception with a message.</summary>
    public J1939NodeException(string message) : base(message) { }

    /// <summary>Constructs an exception with a message and cause.</summary>
    public J1939NodeException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown by <see cref="IJ1939Node.SendAsync"/> when the caller tries to send a message
/// before <see cref="IJ1939Node.ClaimAddressAsync"/> has succeeded (SRS FR-J1939-003/004:
/// only a node with a valid, claimed address may transmit).
/// </summary>
public sealed class J1939NoAddressException : J1939NodeException
{
    /// <summary>Constructs the exception.</summary>
    public J1939NoAddressException()
        : base("This J1939 node has no claimed address; ClaimAddressAsync must succeed before sending.")
    {
    }
}

/// <summary>
/// Thrown by <see cref="IJ1939Node.ClaimAddressAsync"/> when a higher-priority peer contests
/// the preferred address and the node has no fallback (arbitrary addressing disabled or the
/// caller supplied only one candidate). The node transitions to
/// <see cref="J1939ClaimState.CannotClaim"/> and broadcasts Cannot Claim (SA=0xFE) before
/// throwing (SRS FR-J1939-004).
/// </summary>
public sealed class J1939CannotClaimException : J1939NodeException
{
    /// <summary>Constructs the exception with the preferred address that was lost.</summary>
    public J1939CannotClaimException(byte preferredAddress)
        : base($"J1939 node could not claim preferred address 0x{preferredAddress:X2}: contested by a peer with higher-priority NAME.")
    {
        PreferredAddress = preferredAddress;
    }

    /// <summary>The address the caller originally requested.</summary>
    public byte PreferredAddress { get; }
}
