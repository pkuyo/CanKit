using System;
using CanKit.Pro.CANopen.Emcy;
using CanKit.Pro.CANopen.Nmt;

namespace CanKit.Pro.CANopen;

/// <summary>
/// Argument type for the <c>HeartbeatReceived</c> event: a valid CANopen heartbeat with the
/// reported <see cref="State"/> from the producer's NMT slave state machine.
/// </summary>
public sealed class HeartbeatReceivedEventArgs : EventArgs
{
    /// <summary>Producer node-id (derived from COB-ID <c>0x700 + node-id</c>).</summary>
    public byte ProducerNodeId { get; }

    /// <summary>Reported NMT state of the producer. <see cref="NmtState.Initializing"/> is used
    /// for the CiA 301 §7.2.8.3.2 bootup frame (<c>data[0] == 0x00</c>).</summary>
    public NmtState State { get; }

    /// <summary>UTC timestamp captured when the frame was processed on the actor loop.</summary>
    public DateTime Timestamp { get; }

    /// <summary>Constructs a new event.</summary>
    public HeartbeatReceivedEventArgs(byte producerNodeId, NmtState state, DateTime timestamp)
    {
        ProducerNodeId = producerNodeId;
        State = state;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Argument type for the <c>HeartbeatTimeout</c> event: the local heartbeat consumer for
/// <see cref="ProducerNodeId"/> did not observe a heartbeat within the configured timeout.
/// </summary>
public sealed class HeartbeatTimeoutEventArgs : EventArgs
{
    /// <summary>Producer node-id whose heartbeat was missed.</summary>
    public byte ProducerNodeId { get; }

    /// <summary>Configured consumer timeout that elapsed.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Constructs a new event.</summary>
    public HeartbeatTimeoutEventArgs(byte producerNodeId, TimeSpan timeout)
    {
        ProducerNodeId = producerNodeId;
        Timeout = timeout;
    }
}

/// <summary>
/// Argument type for the <c>EmcyReceived</c> event: a decoded incoming EMCY frame.
/// </summary>
public sealed class EmcyReceivedEventArgs : EventArgs
{
    /// <summary>The decoded emergency message (produced by
    /// <see cref="EmcyMessage.Decode(byte, System.ReadOnlySpan{byte})"/>).</summary>
    public EmcyMessage Message { get; }

    /// <summary>UTC timestamp captured when the frame was processed on the actor loop.</summary>
    public DateTime Timestamp { get; }

    /// <summary>Constructs a new event.</summary>
    public EmcyReceivedEventArgs(EmcyMessage message, DateTime timestamp)
    {
        Message = message;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Argument type for the <c>SyncReceived</c> event: a raw incoming SYNC frame.
/// </summary>
public sealed class SyncReceivedEventArgs : EventArgs
{
    /// <summary>UTC timestamp captured when the SYNC was processed on the actor loop.</summary>
    public DateTime Timestamp { get; }

    /// <summary>Constructs a new event.</summary>
    public SyncReceivedEventArgs(DateTime timestamp)
    {
        Timestamp = timestamp;
    }
}

/// <summary>
/// Argument type for the <c>RpdoReceived</c> event: an RPDO whose mapping the local node knows
/// about was received, unpacked and applied to the local Object Dictionary.
/// </summary>
public sealed class RpdoReceivedEventArgs : EventArgs
{
    /// <summary>RPDO index (1..4).</summary>
    public int PdoIndex { get; }

    /// <summary>Raw 11-bit COB-ID the frame arrived on.</summary>
    public uint CobId { get; }

    /// <summary>The full incoming RPDO payload (0..8 bytes).</summary>
    public byte[] Payload { get; }

    /// <summary>Constructs a new event.</summary>
    public RpdoReceivedEventArgs(int pdoIndex, uint cobId, byte[] payload)
    {
        PdoIndex = pdoIndex;
        CobId = cobId;
        Payload = payload;
    }
}

/// <summary>
/// Argument type for the <c>NodeGuardingReceived</c> event: a valid node-guarding response was
/// received from <see cref="ProducerNodeId"/> (FR-CO-009, CiA 301 §7.2.8.3.3).
/// </summary>
public sealed class NodeGuardingReceivedEventArgs : EventArgs
{
    /// <summary>Producer node-id (derived from COB-ID <c>0x700 + node-id</c>).</summary>
    public byte ProducerNodeId { get; }

    /// <summary>Reported NMT state (bits 0..6 of the response byte).</summary>
    public NmtState State { get; }

    /// <summary>Toggle bit (bit 7 of the response byte). CiA 301 §7.2.8.3.3 requires the
    /// producer to flip this on every reply so consumers can distinguish a stale duplicate from
    /// a fresh answer.</summary>
    public bool Toggle { get; }

    /// <summary>UTC timestamp captured when the response was processed on the actor loop.</summary>
    public DateTime Timestamp { get; }

    /// <summary>Constructs a new event.</summary>
    public NodeGuardingReceivedEventArgs(byte producerNodeId, NmtState state, bool toggle, DateTime timestamp)
    {
        ProducerNodeId = producerNodeId;
        State = state;
        Toggle = toggle;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Argument type for the <c>NodeGuardingTimeout</c> event: the configured life-time
/// (<c>guardTime × lifeTimeFactor</c>) elapsed without a fresh response from
/// <see cref="ProducerNodeId"/> (FR-CO-009).
/// </summary>
public sealed class NodeGuardingTimeoutEventArgs : EventArgs
{
    /// <summary>Producer node-id whose life-time elapsed.</summary>
    public byte ProducerNodeId { get; }

    /// <summary>Configured guard-time interval used for the RTR poll.</summary>
    public TimeSpan GuardTime { get; }

    /// <summary>Configured life-time factor. The effective life-time is
    /// <c>GuardTime × LifeTimeFactor</c>.</summary>
    public byte LifeTimeFactor { get; }

    /// <summary>Constructs a new event.</summary>
    public NodeGuardingTimeoutEventArgs(byte producerNodeId, TimeSpan guardTime, byte lifeTimeFactor)
    {
        ProducerNodeId = producerNodeId;
        GuardTime = guardTime;
        LifeTimeFactor = lifeTimeFactor;
    }
}

/// <summary>
/// Argument type for the <c>NmtCommandReceived</c> event: a decoded incoming NMT master command.
/// </summary>
public sealed class NmtCommandReceivedEventArgs : EventArgs
{
    /// <summary>Command specifier (byte 0 of the NMT frame).</summary>
    public NmtCommand Command { get; }

    /// <summary>Target node-id (byte 1); 0 means broadcast to all nodes.</summary>
    public byte TargetNodeId { get; }

    /// <summary>Constructs a new event.</summary>
    public NmtCommandReceivedEventArgs(NmtCommand command, byte targetNodeId)
    {
        Command = command;
        TargetNodeId = targetNodeId;
    }
}
