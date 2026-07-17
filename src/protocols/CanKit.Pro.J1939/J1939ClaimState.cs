using System;

namespace CanKit.Pro.J1939;

/// <summary>
/// The current state of a node's SAE J1939-81 address claim (SRS FR-J1939-003/004).
/// </summary>
public enum J1939ClaimState
{
    /// <summary>The node has no claimed address yet and has not attempted a claim.</summary>
    NotClaimed = 0,

    /// <summary>An Address Claim (PGN 0xEE00) has been transmitted and the node is waiting
    /// the mandatory 250 ms arbitration window (SAE J1939-81 §4.4.3.3).</summary>
    Claiming = 1,

    /// <summary>The node holds the address; other nodes have either yielded or never
    /// contended.</summary>
    Claimed = 2,

    /// <summary>The claim failed and the node sent the Cannot Claim Address broadcast
    /// (source address 0xFE, SAE J1939-81 §4.4.3.4, SRS FR-J1939-004).</summary>
    CannotClaim = 3,
}

/// <summary>
/// Event payload for <see cref="IJ1939Node.AddressClaimChanged"/>. Reports both the new
/// claim state and, if any, the peer that caused a transition.
/// </summary>
public sealed class J1939ClaimEventArgs : EventArgs
{
    /// <summary>Constructs a claim event.</summary>
    public J1939ClaimEventArgs(J1939ClaimState state, byte? address, byte? contendingSourceAddress = null,
        CanKit.Pro.Addressing.J1939Name? contendingName = null)
    {
        State = state;
        Address = address;
        ContendingSourceAddress = contendingSourceAddress;
        ContendingName = contendingName;
    }

    /// <summary>The new claim state.</summary>
    public J1939ClaimState State { get; }

    /// <summary>
    /// The node's own address after the transition, or <c>null</c> when it has none (initial
    /// state or after entering <see cref="J1939ClaimState.CannotClaim"/>).
    /// </summary>
    public byte? Address { get; }

    /// <summary>Source address of the peer that caused this transition, or <c>null</c>.</summary>
    public byte? ContendingSourceAddress { get; }

    /// <summary>NAME of the peer that caused this transition, or <c>null</c>.</summary>
    public CanKit.Pro.Addressing.J1939Name? ContendingName { get; }
}
