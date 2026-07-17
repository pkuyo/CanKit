using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Pro.CANopen.Nmt;
using CanKit.Pro.Reliability;

namespace CanKit.Pro.CANopen;

/// <summary>
/// Node-Guarding (CiA 301 §7.2.8.3.3, FR-CO-009) partial of <see cref="CanOpenNode"/>. Runs
/// on the actor loop like every other protocol subsystem and shares the heartbeat COB-ID
/// range (<c>0x700 + node-id</c>) with the heartbeat producer/consumer.
/// </summary>
/// <remarks>
/// <para>Consumer role: <see cref="StartNodeGuardingConsumer"/> periodically transmits a
/// remote-transmission-request (RTR) frame on <c>0x700 + producerNodeId</c> and arms a
/// life-time deadline of <c>guardTime × lifeTimeFactor</c>. Every valid response rearms the
/// life-time deadline and raises <see cref="ICanOpenNode.NodeGuardingReceived"/>.</para>
/// <para>Producer role: an RTR arriving on <c>0x700 + our node-id</c> is answered with a
/// one-byte data frame whose bit 7 is the alternating toggle bit and bits 0..6 carry the
/// current NMT state. CiA 301 §7.2.8.3 requires heartbeat and node-guarding to be mutually
/// exclusive on a given producer node; this implementation honours that by refusing to reply
/// while the heartbeat producer is active.</para>
/// </remarks>
internal sealed partial class CanOpenNode
{
    /// <inheritdoc />
    public void StartNodeGuardingConsumer(byte producerNodeId, TimeSpan guardTime, byte lifeTimeFactor)
    {
        ThrowIfDisposed();
        CanOpenCobId.ValidateNodeId(producerNodeId);
        if (guardTime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(guardTime), guardTime,
                "guardTime must be positive.");
        if (lifeTimeFactor == 0)
            throw new ArgumentOutOfRangeException(nameof(lifeTimeFactor), lifeTimeFactor,
                "lifeTimeFactor must be >= 1 (CiA 301 §7.2.8.3.3).");

        _actor.Post(() =>
        {
            if (_nodeGuardingConsumers.TryGetValue(producerNodeId, out var existing))
            {
                existing.PollHandle?.Dispose();
                existing.LifeTimeDeadline?.Dispose();
            }
            var consumer = new NodeGuardingConsumer(producerNodeId, guardTime, lifeTimeFactor);
            _nodeGuardingConsumers[producerNodeId] = consumer;
            ScheduleNodeGuardingPoll(consumer);
            var lifeTime = ScaleLifeTime(guardTime, lifeTimeFactor);
            consumer.LifeTimeDeadline = _deadlines.Arm(lifeTime,
                () => OnNodeGuardingTimeout(producerNodeId));
        });
    }

    /// <inheritdoc />
    public void StopNodeGuardingConsumer(byte producerNodeId)
    {
        if (_disposed != 0) return;
        _actor.Post(() =>
        {
            if (_nodeGuardingConsumers.TryGetValue(producerNodeId, out var consumer))
            {
                consumer.PollHandle?.Dispose();
                consumer.LifeTimeDeadline?.Dispose();
                _nodeGuardingConsumers.Remove(producerNodeId);
            }
        });
    }

    // =========================================================================================
    // Consumer helpers.
    // =========================================================================================
    private void ScheduleNodeGuardingPoll(NodeGuardingConsumer consumer)
    {
        // Fire the first poll immediately on the next actor tick so tests do not have to wait
        // an entire guardTime interval for the initial RTR.
        var producer = consumer.ProducerNodeId;
        consumer.PollHandle = _actor.Schedule(consumer.GuardTime, () =>
        {
            try
            {
                if (_disposed != 0) return;
                if (!_nodeGuardingConsumers.TryGetValue(producer, out var current)
                    || !ReferenceEquals(current, consumer))
                {
                    return; // consumer replaced or removed while we slept
                }
                SendNodeGuardingRtr(producer);
            }
            finally
            {
                if (_disposed == 0
                    && _nodeGuardingConsumers.TryGetValue(producer, out var still)
                    && ReferenceEquals(still, consumer))
                {
                    ScheduleNodeGuardingPoll(consumer);
                }
            }
        });
    }

    private void SendNodeGuardingRtr(byte producerNodeId)
    {
        // RTR (remote transmission request) with zero-length payload on 0x700 + producer.
        // Preserving IsRemoteFrame end-to-end depends on the reader loop forwarding it into
        // HandleIncoming and on the adapter (Virtual: preserves via Duplicate) round-tripping it.
        var frame = CanFrame.Classic(
            unchecked((int)CanOpenCobId.Heartbeat(producerNodeId)),
            ReadOnlyMemory<byte>.Empty,
            isExtendedFrame: false,
            isRemoteFrame: true);
        var svc = _service;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var conf = await svc.SendConfirmed(frame).ConfigureAwait(false);
                if (!conf.Confirmed)
                {
                    RaiseBackgroundException(new CanOpenTransportException(
                        $"Node-guarding RTR on COB-ID 0x{CanOpenCobId.Heartbeat(producerNodeId):X3} failed: {conf.FailureReason}."));
                }
            }
            catch (Exception ex) { RaiseBackgroundException(ex); }
        });
    }

    private void OnNodeGuardingTimeout(byte producerNodeId)
    {
        if (!_nodeGuardingConsumers.TryGetValue(producerNodeId, out var consumer)) return;
        // Rearm so subsequent misses still fire.
        consumer.LifeTimeDeadline?.Dispose();
        var lifeTime = ScaleLifeTime(consumer.GuardTime, consumer.LifeTimeFactor);
        consumer.LifeTimeDeadline = _deadlines.Arm(lifeTime,
            () => OnNodeGuardingTimeout(producerNodeId));
        RaiseNodeGuardingTimeout(producerNodeId, consumer.GuardTime, consumer.LifeTimeFactor);
    }

    /// <summary>
    /// Called by <see cref="HandleIncoming"/> when a data frame arrives on
    /// <c>0x700 + producerNodeId</c> and a node-guarding consumer for that producer is
    /// registered. Rearms the life-time deadline and raises the event.
    /// </summary>
    private void HandleNodeGuardingResponse(byte producerNodeId, byte[] data)
    {
        if (data.Length < 1) return;
        if (!_nodeGuardingConsumers.TryGetValue(producerNodeId, out var consumer)) return;

        byte b = data[0];
        bool toggle = (b & 0x80) != 0;
        byte stateByte = (byte)(b & 0x7F);
        NmtState state = stateByte switch
        {
            0x00 => NmtState.Initializing,      // Bootup / freshly reset.
            0x04 => NmtState.Stopped,
            0x05 => NmtState.Operational,
            0x7F => NmtState.PreOperational,
            _ => NmtState.Initializing,
        };

        // Rearm life-time deadline on any valid response.
        var deadline = consumer.LifeTimeDeadline;
        var lifeTime = ScaleLifeTime(consumer.GuardTime, consumer.LifeTimeFactor);
        if (deadline is null || deadline.IsExpired || deadline.IsCancelled || !deadline.Rearm(lifeTime))
        {
            deadline?.Dispose();
            consumer.LifeTimeDeadline = _deadlines.Arm(lifeTime,
                () => OnNodeGuardingTimeout(producerNodeId));
        }

        RaiseNodeGuardingReceived(producerNodeId, state, toggle, DateTime.UtcNow);
    }

    // =========================================================================================
    // Producer role.
    // =========================================================================================
    private void HandleNodeGuardingRtrForSelf()
    {
        if (!_options.RespondToNodeGuardingRtr) return;

        // CiA 301 §7.2.8.3 mandates heartbeat and node-guarding are mutually exclusive on the
        // producer side. If we're actively producing heartbeats, silently ignore the RTR so the
        // consumer falls back on heartbeat error control.
        if (_heartbeatProducerInterval > TimeSpan.Zero) return;

        byte state = (byte)_state;
        byte payload = (byte)((_nodeGuardingProducerToggle ? 0x80 : 0x00) | (state & 0x7F));
        _nodeGuardingProducerToggle = !_nodeGuardingProducerToggle;
        _ = SendControlFrame(CanOpenCobId.Heartbeat(_nodeId), new byte[] { payload });
    }

    private static TimeSpan ScaleLifeTime(TimeSpan guardTime, byte lifeTimeFactor)
    {
        // guardTime * lifeTimeFactor as long-integer ticks; both operands are bounded so
        // overflow is impractical for real-world values, but clamp to TimeSpan.MaxValue just
        // in case an application picks a pathological guardTime.
        long ticks = guardTime.Ticks;
        long scaled;
        try { scaled = checked(ticks * lifeTimeFactor); }
        catch (OverflowException) { scaled = long.MaxValue; }
        return TimeSpan.FromTicks(scaled);
    }

    private void RaiseNodeGuardingReceived(byte producer, NmtState state, bool toggle, DateTime ts)
    {
        var args = new NodeGuardingReceivedEventArgs(producer, state, toggle, ts);
        EnqueueEvent(() =>
        {
            try { NodeGuardingReceived?.Invoke(this, args); }
            catch (Exception ex) { RaiseBackgroundException(ex); }
        });
    }

    private void RaiseNodeGuardingTimeout(byte producer, TimeSpan guardTime, byte lifeTimeFactor)
    {
        var args = new NodeGuardingTimeoutEventArgs(producer, guardTime, lifeTimeFactor);
        EnqueueEvent(() =>
        {
            try { NodeGuardingTimeout?.Invoke(this, args); }
            catch (Exception ex) { RaiseBackgroundException(ex); }
        });
    }

    private sealed class NodeGuardingConsumer
    {
        public NodeGuardingConsumer(byte producerNodeId, TimeSpan guardTime, byte lifeTimeFactor)
        {
            ProducerNodeId = producerNodeId;
            GuardTime = guardTime;
            LifeTimeFactor = lifeTimeFactor;
        }

        public byte ProducerNodeId { get; }
        public TimeSpan GuardTime { get; }
        public byte LifeTimeFactor { get; }
        public IDisposable? PollHandle { get; set; }
        public IDeadline? LifeTimeDeadline { get; set; }
    }
}
