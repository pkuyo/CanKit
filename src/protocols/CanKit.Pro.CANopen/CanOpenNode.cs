using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Pro.Actor;
using CanKit.Pro.CANopen.Emcy;
using CanKit.Pro.CANopen.Nmt;
using CanKit.Pro.CANopen.Pdo;
using CanKit.Pro.CANopen.Sdo;
using CanKit.Pro.RawCan;
using CanKit.Pro.Reliability;

namespace CanKit.Pro.CANopen;

/// <summary>
/// The default <see cref="ICanOpenNode"/> implementation. Composed on the
/// CanKit.Pro L2 pipeline (<see cref="ICanBusService"/> for RX demux and TX confirmation,
/// <see cref="IProtocolActor"/> for single-writer per-node state, <see cref="DeadlineScheduler"/>
/// for SDO/heartbeat/SYNC timers) exactly like the other Pro protocol stacks
/// (arc42 §8.3, ADR-6; FR-CO-012).
/// </summary>
/// <remarks>
/// <para>
/// The node subscribes to the tight set of CANopen 11-bit COB-IDs it can actually receive
/// (NMT master, SYNC, EMCY(any), SDO Rx for its own id, SDO Tx from any peer, heartbeat/bootup
/// from any peer, and every configured RPDO). It never competes on
/// <see cref="ICanBus.ReceiveAsync"/> — RX flows entirely through
/// <see cref="ISubscription.Frames"/>.
/// </para>
/// <para>
/// All state (NMT slave state machine, SDO client/server sessions, heartbeat consumer table,
/// PDO tables, timer handles) lives inside the actor and is only touched from posted callbacks;
/// public methods marshal work in via <see cref="IProtocolActor.PostAsync{T}"/>. This is the
/// same threading model that the J1939-TP / IsoTp / UDS clients rely on.
/// </para>
/// </remarks>
internal sealed class CanOpenNode : ICanOpenNode
{
    private readonly ICanBusService _service;
    private readonly bool _ownsService;
    private readonly byte _nodeId;
    private readonly CanOpenNodeOptions _options;

    private readonly ProtocolActor _actor;
    private readonly DeadlineScheduler _deadlines;
    private readonly ISubscription _subscription;
    private readonly Task _readerTask;
    private readonly CancellationTokenSource _readerCts = new();

    // Bounded, drop-oldest queue that decouples user event delivery from the actor loop.
    // Events (RPDO / EMCY / heartbeat / SYNC / NMT / user-facing signals) are enqueued from
    // the actor thread and drained by a single dispatcher task, so a slow subscriber can never
    // stall the protocol loop. Bounded by CanOpenNodeOptions.EventQueueCapacity; when full,
    // the oldest queued event is dropped (matches the option's documented semantics).
    // BackgroundExceptionOccurred stays synchronous — it is a low-frequency diagnostic signal
    // that should never be silently dropped by queue backpressure.
    private readonly Channel<Action> _eventChannel;
    private readonly Task _eventPumpTask;

    private readonly ObjectDictionary _od = new();

    // -----------------------------------------------------------------------------------------
    // State touched only on the actor loop.
    // -----------------------------------------------------------------------------------------
    private NmtState _state = NmtState.Initializing;

    // SDO server: at most one outstanding segmented transfer against our own OD at a time (a
    // second Initiate from the same peer supersedes any previous open transfer per CiA 301
    // §7.2.4.3.4).
    private SdoServerSession? _sdoServer;

    // SDO client: at most one in-flight client-side transfer per remote server (keyed by the
    // remote node-id we send to). One client concurrently talking to multiple servers is
    // supported (each has its own state entry).
    private readonly Dictionary<byte, SdoClientSession> _sdoClients = new();

    // Heartbeat producer.
    private IDisposable? _heartbeatProducerHandle;
    private TimeSpan _heartbeatProducerInterval;

    // Heartbeat consumers: node-id → (configured timeout, live deadline).
    private readonly Dictionary<byte, HeartbeatConsumer> _heartbeatConsumers = new();

    // SYNC producer.
    private IDisposable? _syncProducerHandle;
    private TimeSpan _syncProducerInterval;

    // PDO tables.
    private readonly Dictionary<int, TpdoConfig> _tpdos = new();
    private readonly Dictionary<uint, RpdoConfig> _rpdosByCobId = new();

    private int _disposed;

    /// <inheritdoc />
    public byte NodeId => _nodeId;

    /// <inheritdoc />
    public CanOpenNodeOptions Options => _options;

    /// <inheritdoc />
    public ObjectDictionary ObjectDictionary => _od;

    /// <inheritdoc />
    public NmtState State
    {
        get
        {
            // When the caller is already on the actor loop (e.g. reading State from a
            // SyncReceived / NmtCommandReceived / RpdoReceived handler that the actor itself
            // is currently running), synchronously waiting on PostAsync would deadlock the loop
            // against itself -- the posted item cannot execute until the current callback
            // returns, but the current callback is blocked here waiting for it. Run the read
            // inline instead: it is the same single-writer thread that any other State write
            // would come from, so no coordination is needed. External callers still marshal
            // through the mailbox to see a value consistent with in-flight transitions.
            if (_actor.IsOnCurrentActor) return _state;
            return _actor.PostAsync(() => _state).GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc />
    public event EventHandler<HeartbeatReceivedEventArgs>? HeartbeatReceived;
    /// <inheritdoc />
    public event EventHandler<HeartbeatTimeoutEventArgs>? HeartbeatTimeout;
    /// <inheritdoc />
    public event EventHandler<EmcyReceivedEventArgs>? EmcyReceived;
    /// <inheritdoc />
    public event EventHandler<SyncReceivedEventArgs>? SyncReceived;
    /// <inheritdoc />
    public event EventHandler<RpdoReceivedEventArgs>? RpdoReceived;
    /// <inheritdoc />
    public event EventHandler<NmtCommandReceivedEventArgs>? NmtCommandReceived;
    /// <inheritdoc />
    public event EventHandler<Exception>? BackgroundExceptionOccurred;

    internal CanOpenNode(ICanBusService service, byte nodeId, CanOpenNodeOptions options,
        bool ownsService)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        CanOpenCobId.ValidateNodeId(nodeId);
        _nodeId = nodeId;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _ownsService = ownsService;

        _actor = new ProtocolActor();
        _actor.BackgroundExceptionOccurred += (_, ex) => RaiseBackgroundException(ex);
        _deadlines = new DeadlineScheduler(_actor);

        // Bounded event queue: drop-oldest keeps steady-state memory constant when a subscriber
        // falls behind, exactly matching the CanOpenNodeOptions.EventQueueCapacity contract.
        _eventChannel = Channel.CreateBounded<Action>(new BoundedChannelOptions(_options.EventQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _eventPumpTask = Task.Run(RunEventPumpAsync);

        try
        {
            // Subscribe once to the tight COB-ID range the node cares about:
            //   0x000 (NMT), 0x080..0x0FF (SYNC + EMCY range),
            //   0x180..0x77F (PDOs + SDO Rx/Tx + heartbeat range).
            // We evaluate the actual routing in the actor since the RPDO table changes at
            // runtime, but pre-filtering at the subscription reduces per-frame delegate calls
            // on the demux side.
            _subscription = _service.Subscribe(f =>
            {
                if (f.IsExtendedFrame) return false;
                uint id = (uint)f.ID;
                // 0x000 NMT master, 0x080..0x77F everything else CANopen.
                return id == CanOpenCobId.NmtCommand || (id >= 0x080 && id <= 0x77F);
            });
        }
        catch
        {
            _actor.Dispose();
            throw;
        }

        _readerTask = Task.Run(RunReaderAsync);

        // Enter Pre-Operational immediately and announce a bootup on the wire. Bootup is a
        // one-shot 1-byte frame with data[0] == 0 on COB-ID 0x700 + nodeId
        // (CiA 301 §7.2.8.3.2). We do it here at construction so tests can observe it.
        _actor.Post(() =>
        {
            _state = NmtState.PreOperational;
            _ = SendControlFrame(CanOpenCobId.Heartbeat(_nodeId), new byte[] { 0x00 });
        });
    }

    /// <inheritdoc />
    public Task SendNmtCommandAsync(NmtCommand command, byte targetNodeId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var payload = new byte[] { (byte)command, targetNodeId };
        return SendControlFrame(CanOpenCobId.NmtCommand, payload, cancellationToken);
    }

    /// <inheritdoc />
    public void StartHeartbeatProducer(TimeSpan interval)
    {
        ThrowIfDisposed();
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _actor.Post(() =>
        {
            _heartbeatProducerHandle?.Dispose();
            _heartbeatProducerInterval = interval;
            ScheduleHeartbeatProducerTick();
        });
    }

    /// <inheritdoc />
    public void StopHeartbeatProducer()
    {
        if (_disposed != 0) return;
        _actor.Post(() =>
        {
            _heartbeatProducerHandle?.Dispose();
            _heartbeatProducerHandle = null;
            _heartbeatProducerInterval = TimeSpan.Zero;
        });
    }

    /// <inheritdoc />
    public void AddHeartbeatConsumer(byte producerNodeId, TimeSpan timeout)
    {
        ThrowIfDisposed();
        CanOpenCobId.ValidateNodeId(producerNodeId);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _actor.Post(() =>
        {
            if (_heartbeatConsumers.TryGetValue(producerNodeId, out var existing))
                existing.Deadline?.Dispose();
            var consumer = new HeartbeatConsumer(producerNodeId, timeout);
            consumer.Deadline = _deadlines.Arm(timeout, () => OnHeartbeatMissed(producerNodeId));
            _heartbeatConsumers[producerNodeId] = consumer;
        });
    }

    /// <inheritdoc />
    public void RemoveHeartbeatConsumer(byte producerNodeId)
    {
        if (_disposed != 0) return;
        _actor.Post(() =>
        {
            if (_heartbeatConsumers.TryGetValue(producerNodeId, out var consumer))
            {
                consumer.Deadline?.Dispose();
                _heartbeatConsumers.Remove(producerNodeId);
            }
        });
    }

    /// <inheritdoc />
    public void StartSyncProducer(TimeSpan interval)
    {
        ThrowIfDisposed();
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _actor.Post(() =>
        {
            _syncProducerHandle?.Dispose();
            _syncProducerInterval = interval;
            ScheduleSyncProducerTick();
        });
    }

    /// <inheritdoc />
    public void StopSyncProducer()
    {
        if (_disposed != 0) return;
        _actor.Post(() =>
        {
            _syncProducerHandle?.Dispose();
            _syncProducerHandle = null;
            _syncProducerInterval = TimeSpan.Zero;
        });
    }

    /// <inheritdoc />
    public Task SendSyncAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return SendControlFrame(CanOpenCobId.Sync, Array.Empty<byte>(), cancellationToken);
    }

    /// <inheritdoc />
    public Task SendEmcyAsync(ushort errorCode, byte errorRegister,
        ReadOnlyMemory<byte> manufacturerSpecific = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var msg = new EmcyMessage(_nodeId, errorCode, errorRegister, manufacturerSpecific.Span);
        return SendControlFrame(CanOpenCobId.Emcy(_nodeId), msg.Encode(), cancellationToken);
    }

    /// <inheritdoc />
    public void ConfigureTpdo(int pdoIndex, PdoMapping mapping,
        TpdoTransmission transmission = TpdoTransmission.EventDriven, uint? cobId = null,
        TimeSpan? eventTimerInterval = null)
    {
        ThrowIfDisposed();
        if (pdoIndex is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(pdoIndex), pdoIndex, "PDO index must be 1..4.");
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));
        var actualCobId = cobId ?? CanOpenCobId.TpdoDefault(_nodeId, pdoIndex);
        var interval = eventTimerInterval ?? _options.DefaultTpdoEventTimerInterval;

        void Apply()
        {
            if (_tpdos.TryGetValue(pdoIndex, out var existing))
                existing.EventTimerHandle?.Dispose();
            var config = new TpdoConfig(pdoIndex, actualCobId, mapping, transmission, interval);
            _tpdos[pdoIndex] = config;
            if (transmission == TpdoTransmission.EventTimer)
                ScheduleTpdoEventTimer(config);
        }

        // A caller already running on the actor loop (e.g. reconfiguring a TPDO from within a
        // SyncReceived / NmtCommandReceived handler) would deadlock the loop against itself if
        // we synchronously waited on PostAsync -- the posted item cannot execute until the
        // current callback returns, but the current callback is stuck waiting for it. Apply
        // inline in that case; it is still the same single-writer thread that any other TPDO
        // config mutation would run on, so no coordination is needed.
        if (_actor.IsOnCurrentActor) Apply();
        else _actor.PostAsync(Apply).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void ConfigureRpdo(int pdoIndex, PdoMapping mapping, uint? cobId = null)
    {
        ThrowIfDisposed();
        if (pdoIndex is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(pdoIndex), pdoIndex, "PDO index must be 1..4.");
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));
        var actualCobId = cobId ?? CanOpenCobId.RpdoDefault(_nodeId, pdoIndex);

        void Apply()
        {
            // Clean out any previous entry that had a different COB-ID for the same slot.
            uint[] existingKeys = new uint[_rpdosByCobId.Count];
            int i = 0;
            foreach (var kv in _rpdosByCobId) existingKeys[i++] = kv.Key;
            foreach (var key in existingKeys)
            {
                if (_rpdosByCobId[key].PdoIndex == pdoIndex) _rpdosByCobId.Remove(key);
            }
            _rpdosByCobId[actualCobId] = new RpdoConfig(pdoIndex, actualCobId, mapping);
        }

        // See ConfigureTpdo above: a caller already on the actor loop (e.g. re-mapping an RPDO
        // from within an NmtCommandReceived handler that transitions the node into Operational)
        // would deadlock the loop against itself if we synchronously waited on PostAsync. Apply
        // inline in that case -- still the same single-writer thread that any other RPDO table
        // mutation would run on.
        if (_actor.IsOnCurrentActor) Apply();
        else _actor.PostAsync(Apply).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public Task TriggerTpdoAsync(int pdoIndex, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _actor.PostAsync(() =>
        {
            if (_state != NmtState.Operational) return;
            if (!_tpdos.TryGetValue(pdoIndex, out var config)) return;
            EmitTpdo(config);
        });
    }

    /// <inheritdoc />
    public Task<byte[]> SdoUploadAsync(byte serverNodeId, ushort index, byte subindex,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CanOpenCobId.ValidateNodeId(serverNodeId);
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisterSdoCancellation(tcs, cancellationToken, serverNodeId);
        _actor.Post(() => BeginSdoUpload(serverNodeId, index, subindex, tcs));
        return tcs.Task;
    }

    /// <inheritdoc />
    public Task SdoDownloadAsync(byte serverNodeId, ushort index, byte subindex,
        ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CanOpenCobId.ValidateNodeId(serverNodeId);
        if (data.Length == 0)
        {
            // The expedited encoding steals bit-pair "n" from the CS byte to advertise how many
            // of the four payload bytes are valid (n = 4 - length, 2 bits). Length 0 collapses
            // onto the same wire encoding as length 4 (n = 0), so a length-0 expedited download
            // is indistinguishable from a length-4 one on the receiver. Rather than silently
            // sending a bogus 4-byte frame that either a) writes four zero bytes on the peer or
            // b) trips a length-mismatch abort, we reject empty downloads here with a clear
            // exception. Callers wanting to touch an OD entry without changing its value should
            // use a segmented transport (e.g. by embedding at least one meaningful byte).
            throw new ArgumentException(
                "SDO download payload must contain at least one byte; the CiA 301 expedited " +
                "encoding cannot represent a zero-length download and empty payloads are " +
                "rejected rather than being silently misencoded as four zero bytes.",
                nameof(data));
        }
        var payload = data.ToArray();
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisterSdoCancellation(tcs, cancellationToken, serverNodeId);
        _actor.Post(() => BeginSdoDownload(serverNodeId, index, subindex, payload, tcs));
        return tcs.Task;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _readerCts.Cancel(); } catch { /* nothing else to do */ }

        try
        {
            _actor.Post(() =>
            {
                _heartbeatProducerHandle?.Dispose();
                _heartbeatProducerHandle = null;
                _syncProducerHandle?.Dispose();
                _syncProducerHandle = null;
                foreach (var kv in _heartbeatConsumers) kv.Value.Deadline?.Dispose();
                _heartbeatConsumers.Clear();
                foreach (var kv in _tpdos) kv.Value.EventTimerHandle?.Dispose();
                _tpdos.Clear();
                _rpdosByCobId.Clear();

                _sdoServer?.Deadline?.Dispose();
                _sdoServer = null;
                foreach (var kv in _sdoClients)
                {
                    kv.Value.Deadline?.Dispose();
                    kv.Value.Tcs.TrySetException(new ObjectDisposedException(nameof(CanOpenNode)));
                }
                _sdoClients.Clear();
            });
        }
        catch (ObjectDisposedException)
        {
            // actor already gone; nothing more to do
        }

        try { _readerTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* observed via task; not fatal */ }

        // Complete the event channel so the pump task exits after draining anything still
        // queued. This keeps ordering: any event enqueued before Dispose is guaranteed to be
        // delivered before the pump exits (unless the subscriber itself hangs), while further
        // TryWrite calls (a stray raise from an actor callback still winding down) simply
        // return false — dropped rather than kept alive past Dispose.
        _eventChannel.Writer.TryComplete();
        try { _eventPumpTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* observed via task; not fatal */ }

        _subscription.Dispose();
        _actor.Dispose();
        _readerCts.Dispose();

        if (_ownsService) _service.Dispose();
    }

    // =========================================================================================
    // Subscription reader -- hands off to the actor loop.
    // =========================================================================================
    private async Task RunReaderAsync()
    {
        try
        {
            await foreach (var frame in _subscription.Frames.WithCancellation(_readerCts.Token)
                .ConfigureAwait(false))
            {
                if (frame.IsExtendedFrame) continue;
                uint id = (uint)frame.ID;
                var data = frame.Data.ToArray();
                _actor.Post(() => HandleIncoming(id, data));
            }
        }
        catch (OperationCanceledException) { /* Dispose */ }
        catch (Exception ex) { RaiseBackgroundException(ex); }
    }

    // =========================================================================================
    // Event pump — dispatches queued RPDO / EMCY / heartbeat / SYNC / NMT events on its own
    // task so that a slow subscriber never blocks the actor loop or the RX reader. Runs a
    // single reader so events are delivered in enqueue order.
    // =========================================================================================
    private async Task RunEventPumpAsync()
    {
        try
        {
            while (await _eventChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_eventChannel.Reader.TryRead(out var raise))
                {
                    try { raise(); }
                    catch (Exception ex) { RaiseBackgroundException(ex); }
                }
            }
        }
        catch (OperationCanceledException) { /* Dispose */ }
        catch (Exception ex) { RaiseBackgroundException(ex); }
    }

    private void EnqueueEvent(Action raise)
    {
        // DropOldest mode: TryWrite either enqueues or silently discards the oldest queued
        // event to make room. It never blocks and never throws. Once the channel is completed
        // (Dispose), TryWrite returns false and the raise is dropped, which is what we want:
        // no delivery guarantee is documented past disposal.
        _eventChannel.Writer.TryWrite(raise);
    }

    private void HandleIncoming(uint cobId, byte[] data)
    {
        try
        {
            if (cobId == CanOpenCobId.NmtCommand)
            {
                HandleNmtCommand(data);
                return;
            }
            if (cobId == CanOpenCobId.Sync)
            {
                HandleSync();
                return;
            }
            // EMCY 0x081..0x0FF (0x080 is SYNC and is handled above).
            if (cobId is >= 0x081 and <= 0x0FF)
            {
                HandleEmcy(cobId, data);
                return;
            }
            // Heartbeat / bootup 0x701..0x77F.
            if (cobId is >= 0x701 and <= 0x77F)
            {
                HandleHeartbeat(cobId, data);
                return;
            }
            // SDO client request: 0x600 + our nodeId — targeted at *our* SDO server.
            if (cobId == CanOpenCobId.SdoRx(_nodeId))
            {
                HandleSdoServerRequest(data);
                return;
            }
            // SDO server response: 0x580 + serverNodeId — our SDO *client* is the recipient.
            if (cobId is >= CanOpenCobId.SdoTxBase + CanOpenCobId.MinNodeId
                and <= CanOpenCobId.SdoTxBase + CanOpenCobId.MaxNodeId)
            {
                byte serverNodeId = (byte)(cobId - CanOpenCobId.SdoTxBase);
                HandleSdoClientResponse(serverNodeId, data);
                return;
            }
            // RPDO?
            if (_rpdosByCobId.TryGetValue(cobId, out var rpdo))
            {
                HandleRpdo(rpdo, data);
                return;
            }
        }
        catch (Exception ex)
        {
            RaiseBackgroundException(ex);
        }
    }

    // =========================================================================================
    // NMT slave state machine (FR-CO-007)
    // =========================================================================================
    private void HandleNmtCommand(byte[] data)
    {
        if (data.Length < 2) return;
        var cmd = (NmtCommand)data[0];
        byte target = data[1];
        // 0 = broadcast (all nodes); otherwise apply only when the target is us.
        bool forUs = target == 0 || target == _nodeId;
        RaiseNmtCommandReceived(cmd, target);
        if (!forUs) return;

        switch (cmd)
        {
            case NmtCommand.Start:
                _state = NmtState.Operational;
                break;
            case NmtCommand.Stop:
                _state = NmtState.Stopped;
                break;
            case NmtCommand.EnterPreOperational:
                _state = NmtState.PreOperational;
                break;
            case NmtCommand.ResetNode:
            case NmtCommand.ResetCommunication:
                // MVP: reset acts like re-init → emit a bootup and settle in Pre-Op.
                _state = NmtState.Initializing;
                _ = SendControlFrame(CanOpenCobId.Heartbeat(_nodeId), new byte[] { 0x00 });
                _state = NmtState.PreOperational;
                break;
        }
        // Send a heartbeat immediately reflecting the new state so consumers see the transition
        // without waiting on the periodic tick.
        _ = SendControlFrame(CanOpenCobId.Heartbeat(_nodeId), new byte[] { (byte)_state });
    }

    // =========================================================================================
    // SYNC (FR-CO-010)
    // =========================================================================================
    private void HandleSync()
    {
        RaiseSyncReceived(DateTime.UtcNow);
        // Emit every synchronous TPDO in a deterministic (index-ascending) order.
        var indices = new int[_tpdos.Count];
        int i = 0;
        foreach (var key in _tpdos.Keys) indices[i++] = key;
        Array.Sort(indices);
        foreach (var idx in indices)
        {
            var t = _tpdos[idx];
            if (t.Transmission == TpdoTransmission.Synchronous && _state == NmtState.Operational)
                EmitTpdo(t);
        }
    }

    private void ScheduleSyncProducerTick()
    {
        if (_syncProducerInterval <= TimeSpan.Zero) return;
        _syncProducerHandle = _actor.Schedule(_syncProducerInterval, () =>
        {
            try
            {
                if (_disposed != 0) return;
                _ = SendControlFrame(CanOpenCobId.Sync, Array.Empty<byte>());
            }
            finally
            {
                if (_disposed == 0 && _syncProducerInterval > TimeSpan.Zero)
                    ScheduleSyncProducerTick();
            }
        });
    }

    // =========================================================================================
    // EMCY (FR-CO-011)
    // =========================================================================================
    private void HandleEmcy(uint cobId, byte[] data)
    {
        if (data.Length < EmcyMessage.WireSize) return;
        byte producer = (byte)(cobId - CanOpenCobId.EmcyBase);
        var msg = EmcyMessage.Decode(producer, data);
        RaiseEmcyReceived(msg, DateTime.UtcNow);
    }

    // =========================================================================================
    // Heartbeat (FR-CO-008)
    // =========================================================================================
    private void HandleHeartbeat(uint cobId, byte[] data)
    {
        if (data.Length < 1) return;
        byte producer = (byte)(cobId - CanOpenCobId.HeartbeatBase);
        byte stateByte = (byte)(data[0] & 0x7F); // Toggle bit is bit 7; we do not use it in MVP.
        NmtState state = stateByte switch
        {
            0x00 => NmtState.Initializing,      // Bootup frame.
            0x04 => NmtState.Stopped,
            0x05 => NmtState.Operational,
            0x7F => NmtState.PreOperational,
            _ => NmtState.Initializing,
        };
        RaiseHeartbeatReceived(producer, state, DateTime.UtcNow);
        if (_heartbeatConsumers.TryGetValue(producer, out var consumer))
        {
            // Rearm the deadline — best-effort. On failure, allocate a fresh one to preserve
            // the semantic "if we do not see another heartbeat within timeout, fire".
            var deadline = consumer.Deadline;
            if (deadline is null || deadline.IsExpired || deadline.IsCancelled || !deadline.Rearm(consumer.Timeout))
            {
                deadline?.Dispose();
                consumer.Deadline = _deadlines.Arm(consumer.Timeout, () => OnHeartbeatMissed(producer));
            }
        }
    }

    private void OnHeartbeatMissed(byte producerNodeId)
    {
        if (!_heartbeatConsumers.TryGetValue(producerNodeId, out var consumer)) return;
        // Rearm so subsequent misses still fire; consumer explicitly re-registered on every
        // heartbeat receipt above, but if the heartbeat is completely absent we keep firing.
        consumer.Deadline?.Dispose();
        consumer.Deadline = _deadlines.Arm(consumer.Timeout, () => OnHeartbeatMissed(producerNodeId));
        RaiseHeartbeatTimeout(producerNodeId, consumer.Timeout);
    }

    private void ScheduleHeartbeatProducerTick()
    {
        if (_heartbeatProducerInterval <= TimeSpan.Zero) return;
        _heartbeatProducerHandle = _actor.Schedule(_heartbeatProducerInterval, () =>
        {
            try
            {
                if (_disposed != 0) return;
                _ = SendControlFrame(CanOpenCobId.Heartbeat(_nodeId), new byte[] { (byte)_state });
            }
            finally
            {
                if (_disposed == 0 && _heartbeatProducerInterval > TimeSpan.Zero)
                    ScheduleHeartbeatProducerTick();
            }
        });
    }

    // =========================================================================================
    // SDO server (FR-CO-002 / FR-CO-003 / FR-CO-001 access-check)
    // =========================================================================================
    private void HandleSdoServerRequest(byte[] data)
    {
        if (data.Length == 0) return; // nothing to look at — can't even read the CS byte
        if (data.Length < 8)
        {
            // Match the symmetric client-side handling (see HandleSdoClientResponse): pad
            // trailing-zero-stripped SDO frames to 8 bytes rather than dropping them, since
            // SdoFrames' decoders are happy to read the shorter versions and the trailing
            // bytes are always unused/zero in the current MVP frame formats. Prior behaviour
            // left a well-formed but short client initiate lingering on the wire until its
            // client-side timeout, which is exactly the bug Copilot flagged for the client
            // path — this keeps the server path from having the mirror-image problem.
            var padded = new byte[8];
            Buffer.BlockCopy(data, 0, padded, 0, data.Length);
            data = padded;
        }
        byte cs = data[0];

        // Abort from the peer ends any active server session.
        if (cs == SdoFrames.CsAbort)
        {
            _sdoServer?.Deadline?.Dispose();
            _sdoServer = null;
            return;
        }

        // Client's segment for an in-flight download.
        if ((cs & 0xE0) == SdoFrames.CcsDownloadSegmentBase && _sdoServer is { InDownload: true } dl)
        {
            HandleServerDownloadSegment(dl, data);
            return;
        }

        // Client's segment ack for an in-flight upload.
        if ((cs & 0xE0) == SdoFrames.CcsUploadSegmentBase && _sdoServer is { InDownload: false } ul)
        {
            HandleServerUploadSegmentRequest(ul, data);
            return;
        }

        var (index, subindex) = SdoFrames.ReadIndex(data);
        _od.TryGet(index, subindex, out var entry);

        // Upload init (client → server).
        if (cs == SdoFrames.CcsUploadInit)
        {
            HandleServerUploadInit(index, subindex, entry);
            return;
        }
        // Download init (client → server). Cs matches expedited (0x2X) or segmented (0x21).
        if (cs == SdoFrames.CcsDownloadInitSegmented || (cs & 0xE0) == SdoFrames.CcsDownloadInitExpeditedBase)
        {
            HandleServerDownloadInit(index, subindex, entry, cs, data);
            return;
        }

        // Anything else is a protocol error → abort.
        SendSdoServerAbort(index, subindex, SdoAbortCode.CommandSpecifierInvalid);
    }

    private void HandleServerUploadInit(ushort index, byte subindex, OdEntry? entry)
    {
        // Per CiA 301 §7.2.4.3.4 a fresh initiate implicitly supersedes any previously open
        // server-side transfer. Emit an abort on the wire for that stale (index, subindex) so
        // its remote client does not have to wait for its own SDO timeout to notice the
        // supersede. Done up-front so this holds even when we go on to reject the new initiate
        // below (missing entry, wrong access, ...).
        AbortSupersededServerSession();

        if (entry is null)
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.ObjectDoesNotExist);
            return;
        }
        if ((entry.Access & OdAccess.ReadOnly) == 0)
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.AttemptReadWriteOnly);
            return;
        }

        var value = entry.GetRawValue();
        // Expedited only makes sense for 1..4 bytes: the 2-bit "n" field in the CS byte cannot
        // distinguish a 0-byte payload from a 4-byte payload (both encode as n=0), so a
        // length-0 OD value would decode as four zeros on the peer. Route empty values through
        // the segmented path where the size indicator is a full 32-bit little-endian field,
        // and a "last-segment / n=7" segment cleanly carries zero data bytes to the client.
        if (value.Length is >= 1 and <= 4)
        {
            // Expedited upload response — no server-side session survives the initiate, since
            // the whole value fits in the response frame. (Supersede handling already ran at
            // the top of the method, so no stale session remains.)
            var buf = new byte[8];
            buf[0] = (byte)(SdoFrames.ScsUploadInitExpeditedBase | (((4 - value.Length) & 0x03) << 2) | 0x03);
            buf[1] = (byte)(index & 0xFF);
            buf[2] = (byte)((index >> 8) & 0xFF);
            buf[3] = subindex;
            for (int i = 0; i < value.Length; i++) buf[4 + i] = value[i];
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), buf);
            return;
        }

        // Segmented upload: reply with 0x41 + size, then serve segments as the client acks.
        // Also used for value.Length == 0 (the "empty OD value" case): declared length 0, and
        // the first segment request from the client will be answered with a "last, 0-byte"
        // segment (n=7, c=1) which decodes cleanly to an empty payload.
        var initBuf = new byte[8];
        initBuf[0] = SdoFrames.ScsUploadInitSegmented;
        initBuf[1] = (byte)(index & 0xFF);
        initBuf[2] = (byte)((index >> 8) & 0xFF);
        initBuf[3] = subindex;
        uint len = (uint)value.Length;
        initBuf[4] = (byte)(len & 0xFF);
        initBuf[5] = (byte)((len >> 8) & 0xFF);
        initBuf[6] = (byte)((len >> 16) & 0xFF);
        initBuf[7] = (byte)((len >> 24) & 0xFF);

        var session = new SdoServerSession(inDownload: false, index, subindex, value, offset: 0, toggle: false);
        _sdoServer = session;
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), initBuf);
    }

    private void HandleServerUploadSegmentRequest(SdoServerSession session, byte[] data)
    {
        byte cs = data[0];
        bool toggleReq = (cs & SdoFrames.ToggleBit) != 0;
        if (toggleReq != session.Toggle)
        {
            SendSdoServerAbort(session.Index, session.Subindex, SdoAbortCode.ToggleBitNotAlternated);
            _sdoServer = null;
            return;
        }

        int remaining = session.Buffer.Length - session.Offset;
        int chunk = Math.Min(7, remaining);
        var payload = new byte[chunk];
        Buffer.BlockCopy(session.Buffer, session.Offset, payload, 0, chunk);
        bool last = (session.Offset + chunk) >= session.Buffer.Length;
        var seg = SdoFrames.BuildSegment(SdoFrames.ScsUploadSegmentBase, session.Toggle, last, payload);
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), seg);

        session.Offset += chunk;
        session.Toggle = !session.Toggle;
        if (last)
            _sdoServer = null;
    }

    private void HandleServerDownloadInit(ushort index, byte subindex, OdEntry? entry, byte cs, byte[] data)
    {
        // Per CiA 301 §7.2.4.3.4 a fresh initiate implicitly supersedes any previously open
        // server-side transfer. Emit an abort on the wire for that stale (index, subindex) so
        // its remote client does not have to wait for its own SDO timeout to notice the
        // supersede. Done up-front so this holds even when we go on to reject the new initiate
        // below (missing entry, wrong access, length mismatch, ...).
        AbortSupersededServerSession();

        if (entry is null)
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.ObjectDoesNotExist);
            return;
        }
        if ((entry.Access & OdAccess.WriteOnly) == 0)
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.AttemptWriteReadOnly);
            return;
        }

        if (cs == SdoFrames.CcsDownloadInitSegmented)
        {
            // Segmented download — reply Init-Ack, prepare a growing buffer with declared size.
            uint declaredLen = (uint)(data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24));

            // Cap the initiator's 32-bit declared length before the `new byte[declaredLen]`
            // below: a hostile / buggy peer can otherwise drive us into an unbounded allocation
            // (up to 4 GiB) purely by choosing the bytes in the init frame's size field. Beyond
            // the option cap the CiA 301 "out of memory" abort code (0x05040005) is the right
            // signal to send back to the peer. See Bugbot 3600644166.
            if (declaredLen > (uint)_options.MaxSdoTransferBytes)
            {
                SendSdoServerAbort(index, subindex, SdoAbortCode.OutOfMemory);
                return;
            }

            // For fixed-width types the declared length must equal the OD's declared width.
            int fixedSize = OdEntryLayout.FixedSize(entry.DataType);
            if (fixedSize > 0 && declaredLen != fixedSize)
            {
                var reason = declaredLen > fixedSize
                    ? SdoAbortCode.LengthTooHigh : SdoAbortCode.LengthTooLow;
                SendSdoServerAbort(index, subindex, reason);
                return;
            }

            // Supersede handling already ran at the top of the method; install the fresh
            // segmented-download session cleanly here.
            _sdoServer = new SdoServerSession(inDownload: true, index, subindex,
                new byte[declaredLen], offset: 0, toggle: false);
            var ack = new byte[8];
            ack[0] = SdoFrames.ScsDownloadInitAck;
            ack[1] = (byte)(index & 0xFF);
            ack[2] = (byte)((index >> 8) & 0xFF);
            ack[3] = subindex;
            _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), ack);
            return;
        }

        // Expedited download.
        var payload = SdoFrames.ReadExpeditedPayload(data);
        int required = OdEntryLayout.FixedSize(entry.DataType);
        if (required > 0 && payload.Length != required)
        {
            var reason = payload.Length > required
                ? SdoAbortCode.LengthTooHigh : SdoAbortCode.LengthTooLow;
            SendSdoServerAbort(index, subindex, reason);
            return;
        }
        // Commit the value BEFORE acknowledging so a client task that unblocks on our ACK
        // and then reads the OD (on any thread) is guaranteed to observe the new value.
        // Route the write through ObjectDictionary.WriteRaw so it lands under the OD's
        // internal lock, giving readers on other threads a proper acquire/release pairing
        // rather than relying on OdEntry field-level memory ordering.
        try { _od.WriteRaw(index, subindex, payload); }
        catch { SendSdoServerAbort(index, subindex, SdoAbortCode.General); return; }

        // Expedited download owns no ongoing segmented state — no server-side session survives
        // the initiate. Supersede handling for any previously open segmented transfer already
        // ran at the top of the method (see AbortSupersededServerSession there); nothing left
        // to clear here beyond acknowledging the write on the wire.
        var respBuf = new byte[8];
        respBuf[0] = SdoFrames.ScsDownloadInitAck;
        respBuf[1] = (byte)(index & 0xFF);
        respBuf[2] = (byte)((index >> 8) & 0xFF);
        respBuf[3] = subindex;
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), respBuf);
    }

    private void HandleServerDownloadSegment(SdoServerSession session, byte[] data)
    {
        var (payload, last, toggle) = SdoFrames.ReadSegment(data);
        if (toggle != session.Toggle)
        {
            SendSdoServerAbort(session.Index, session.Subindex, SdoAbortCode.ToggleBitNotAlternated);
            _sdoServer = null;
            return;
        }
        if (session.Offset + payload.Length > session.Buffer.Length)
        {
            SendSdoServerAbort(session.Index, session.Subindex, SdoAbortCode.LengthTooHigh);
            _sdoServer = null;
            return;
        }
        Buffer.BlockCopy(payload, 0, session.Buffer, session.Offset, payload.Length);
        session.Offset += payload.Length;

        // On the last segment, commit the accumulated buffer to the OD BEFORE ACKing.
        //
        // SendControlFrame dispatches the wire TX through Task.Run, so the ACK can be
        // delivered to the peer on a ThreadPool thread concurrently with this actor
        // continuing. If we ACK first, the client's SdoDownloadAsync task completes as
        // soon as the ACK arrives, which lets its caller read the OD before the actor
        // reaches the SetRawValue below -- exactly the race that used to leave the
        // segmented-download round-trip test intermittently observing all zeros on net48.
        //
        // Routing the write through ObjectDictionary.WriteRaw also takes the OD's lock,
        // giving readers on other threads a proper release/acquire pairing rather than
        // relying on field-level memory ordering of OdEntry._value.
        if (last)
        {
            if (session.Offset != session.Buffer.Length)
            {
                SendSdoServerAbort(session.Index, session.Subindex, SdoAbortCode.LengthTooLow);
                _sdoServer = null;
                return;
            }
            var final = new byte[session.Offset];
            Buffer.BlockCopy(session.Buffer, 0, final, 0, session.Offset);
            try { _od.WriteRaw(session.Index, session.Subindex, final); }
            catch
            {
                SendSdoServerAbort(session.Index, session.Subindex, SdoAbortCode.General);
                return;
            }
        }

        // Server segment ack -- sent after any OD commit so the client can never observe
        // "download finished" before the OD is up to date.
        byte cs = SdoFrames.ScsDownloadSegmentBase;
        if (session.Toggle) cs |= SdoFrames.ToggleBit;
        var ack = new byte[8];
        ack[0] = cs;
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), ack);
        session.Toggle = !session.Toggle;

        if (last) _sdoServer = null;
    }

    private void SendSdoServerAbort(ushort index, byte subindex, SdoAbortCode code)
    {
        _sdoServer?.Deadline?.Dispose();
        _sdoServer = null;
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
            SdoFrames.BuildAbort(index, subindex, (uint)code));
    }

    /// <summary>
    /// Aborts any currently-open SDO server transfer on the wire and clears the local session.
    /// Called before installing or omitting a new server session on any client-initiated
    /// initiate (upload or download, expedited or segmented). Per CiA 301 §7.2.4.3.4 the new
    /// initiate implicitly supersedes the previously open transfer, but staying silent leaves
    /// the remote client blocked on its own SDO timer for that superseded transfer. Emitting a
    /// General abort against the previously open (index, subindex) unblocks that peer
    /// immediately, matching what compliant CiA 301 servers do (e.g. CANopenNode).
    /// </summary>
    private void AbortSupersededServerSession()
    {
        var stale = _sdoServer;
        if (stale is null) return;
        stale.Deadline?.Dispose();
        _sdoServer = null;
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId),
            SdoFrames.BuildAbort(stale.Index, stale.Subindex, (uint)SdoAbortCode.General));
    }

    // =========================================================================================
    // SDO client (FR-CO-002 / FR-CO-003)
    // =========================================================================================
    private void RegisterSdoCancellation<T>(TaskCompletionSource<T> tcs, CancellationToken ct,
        byte serverNodeId)
    {
        if (!ct.CanBeCanceled) return;
        ct.Register(static state =>
        {
            var (self, sid, boxed, token) = ((CanOpenNode, byte, object, CancellationToken))state!;
            try
            {
                self._actor.Post(() => self.CancelSdoClient(sid, boxed, token));
            }
            catch (ObjectDisposedException)
            {
                if (boxed is TaskCompletionSource<byte[]> tcs1) tcs1.TrySetCanceled(token);
            }
        }, (this, serverNodeId, (object)tcs, ct));
    }

    private void CancelSdoClient(byte serverNodeId, object tcsBoxed, CancellationToken token)
    {
        if (_sdoClients.TryGetValue(serverNodeId, out var session) && ReferenceEquals(session.Tcs, tcsBoxed))
        {
            _sdoClients.Remove(serverNodeId);
            session.Deadline?.Dispose();
            _ = SendControlFrame(CanOpenCobId.SdoRx(serverNodeId),
                SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)SdoAbortCode.General));
        }
        if (tcsBoxed is TaskCompletionSource<byte[]> tcs) tcs.TrySetCanceled(token);
    }

    private void BeginSdoUpload(byte serverNodeId, ushort index, byte subindex,
        TaskCompletionSource<byte[]> tcs)
    {
        if (_disposed != 0)
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(CanOpenNode)));
            return;
        }
        if (tcs.Task.IsCompleted) return;
        if (_sdoClients.ContainsKey(serverNodeId))
        {
            tcs.TrySetException(new InvalidOperationException(
                $"An SDO transfer with server 0x{serverNodeId:X2} is already in flight."));
            return;
        }
        var session = new SdoClientSession(serverNodeId, index, subindex, isDownload: false,
            payload: null, tcs);
        _sdoClients[serverNodeId] = session;
        session.Deadline = _deadlines.Arm(_options.SdoTimeout, () => OnSdoClientTimeout(serverNodeId));
        _ = SendControlFrame(CanOpenCobId.SdoRx(serverNodeId), SdoFrames.BuildUploadInit(index, subindex));
    }

    private void BeginSdoDownload(byte serverNodeId, ushort index, byte subindex,
        byte[] payload, TaskCompletionSource<byte[]> tcs)
    {
        if (_disposed != 0)
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(CanOpenNode)));
            return;
        }
        if (tcs.Task.IsCompleted) return;
        if (_sdoClients.ContainsKey(serverNodeId))
        {
            tcs.TrySetException(new InvalidOperationException(
                $"An SDO transfer with server 0x{serverNodeId:X2} is already in flight."));
            return;
        }
        var session = new SdoClientSession(serverNodeId, index, subindex, isDownload: true,
            payload, tcs);
        _sdoClients[serverNodeId] = session;
        session.Deadline = _deadlines.Arm(_options.SdoTimeout, () => OnSdoClientTimeout(serverNodeId));
        _ = SendControlFrame(CanOpenCobId.SdoRx(serverNodeId),
            SdoFrames.BuildDownloadInit(index, subindex, payload));
    }

    private void OnSdoClientTimeout(byte serverNodeId)
    {
        if (!_sdoClients.TryGetValue(serverNodeId, out var session)) return;
        _sdoClients.Remove(serverNodeId);
        session.Deadline?.Dispose();
        // Send a client-side abort so the server knows to drop any lingering state.
        _ = SendControlFrame(CanOpenCobId.SdoRx(serverNodeId),
            SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)SdoAbortCode.SdoProtocolTimedOut));
        session.Tcs.TrySetException(new SdoAbortException(session.Index, session.Subindex,
            SdoAbortCode.SdoProtocolTimedOut));
    }

    private void HandleSdoClientResponse(byte serverNodeId, byte[] data)
    {
        if (!_sdoClients.TryGetValue(serverNodeId, out var session)) return;
        if (data.Length == 0) return; // nothing to look at — can't even read the CS byte
        if (data.Length < 8)
        {
            // Real-world ECUs sometimes strip trailing zero bytes under DLC-padding rules
            // (esp. on CAN-FD gateways that translate short SDO responses). SdoFrames.Read*
            // is happy to decode as long as it can reach the fields it needs, so pad the
            // frame to 8 bytes rather than silently dropping it and letting the client hang
            // on its SDO deadline. Trailing zeros are semantically the same as "unused
            // bytes" in every SDO frame layout in the MVP (index/sub, abort code, expedited
            // payload with n = 4 − DLC-4, segment payload with n = 7 − (DLC-1)).
            var padded = new byte[8];
            Buffer.BlockCopy(data, 0, padded, 0, data.Length);
            data = padded;
        }
        byte cs = data[0];

        if (cs == SdoFrames.CsAbort)
        {
            var (idx, sub) = SdoFrames.ReadIndex(data);
            uint code = SdoFrames.ReadAbortCode(data);
            // The abort may target a DIFFERENT transfer than ours: a compliant CiA 301 §7.2.4.3.4
            // server emits an abort for a previously open transfer (potentially from another
            // client, or an abandoned older initiate from us) when a new initiate supersedes it.
            // Only fail our own session when the abort references the (index, subindex) we are
            // actually asking about; otherwise it is bookkeeping for a transfer that is not ours
            // and we ignore it so the response for our real request can still complete us.
            if (idx != session.Index || sub != session.Subindex) return;
            _sdoClients.Remove(serverNodeId);
            session.Deadline?.Dispose();
            session.Tcs.TrySetException(new SdoAbortException(idx, sub, code,
                $"Peer server 0x{serverNodeId:X2} aborted SDO transfer 0x{idx:X4}:{sub:X2} with code 0x{code:X8}."));
            return;
        }

        // Rearm the client timeout on any valid response byte we see.
        var deadline = session.Deadline;
        if (deadline is null || deadline.IsExpired || deadline.IsCancelled || !deadline.Rearm(_options.SdoTimeout))
        {
            deadline?.Dispose();
            session.Deadline = _deadlines.Arm(_options.SdoTimeout, () => OnSdoClientTimeout(serverNodeId));
        }

        if (session.IsDownload)
        {
            if (cs == SdoFrames.ScsDownloadInitAck)
            {
                // For expedited download this completes the transfer. For segmented, start
                // sending segments (or complete if the payload is empty, though we always used
                // expedited for zero-length data).
                if (session.Payload!.Length <= 4)
                {
                    _sdoClients.Remove(serverNodeId);
                    session.Deadline?.Dispose();
                    session.Tcs.TrySetResult(Array.Empty<byte>());
                    return;
                }
                SendNextClientDownloadSegment(session);
                return;
            }
            // Server segment ack.
            if ((cs & 0xE0) == SdoFrames.ScsDownloadSegmentBase)
            {
                bool toggleAck = (cs & SdoFrames.ToggleBit) != 0;
                if (toggleAck != session.Toggle)
                {
                    AbortClient(session, SdoAbortCode.ToggleBitNotAlternated);
                    return;
                }
                session.Toggle = !session.Toggle;
                if (session.Offset >= session.Payload!.Length)
                {
                    _sdoClients.Remove(serverNodeId);
                    session.Deadline?.Dispose();
                    session.Tcs.TrySetResult(Array.Empty<byte>());
                    return;
                }
                SendNextClientDownloadSegment(session);
                return;
            }
        }
        else
        {
            // Upload path — expected: expedited response 0x43/0x4B/0x4F/0x47/0x4B, or segmented
            // init 0x41, or segment 0x00/0x10/0x0X/0x1X.
            if ((cs & 0xE0) == SdoFrames.ScsUploadInitExpeditedBase && (cs & 0x02) != 0)
            {
                // Expedited upload complete.
                var value = SdoFrames.ReadExpeditedPayload(data);
                _sdoClients.Remove(serverNodeId);
                session.Deadline?.Dispose();
                session.Tcs.TrySetResult(value);
                return;
            }
            if (cs == SdoFrames.ScsUploadInitSegmented)
            {
                uint declared = SdoFrames.ReadSegmentedTotalLength(data);
                // Cap the server's 32-bit declared length before the `new byte[declared]`
                // below. Mirrors the server-side cap in HandleServerDownloadInit above: a
                // hostile / buggy server can otherwise coax the client into an unbounded
                // allocation just by choosing the size bytes in the segmented upload-init
                // response. Abort back to the server with the CiA 301 "out of memory" code
                // (0x05040005) and fail the client task with the same abort. See Bugbot
                // 3600644166.
                if (declared > (uint)_options.MaxSdoTransferBytes)
                {
                    AbortClient(session, SdoAbortCode.OutOfMemory);
                    return;
                }
                session.Payload = declared > 0 ? new byte[declared] : Array.Empty<byte>();
                session.Offset = 0;
                session.Toggle = false;
                SendNextClientUploadSegmentRequest(session);
                return;
            }
            if ((cs & 0xE0) == SdoFrames.ScsUploadSegmentBase && (cs & 0x40) == 0)
            {
                var (payload, last, toggle) = SdoFrames.ReadSegment(data);
                if (toggle != session.Toggle)
                {
                    AbortClient(session, SdoAbortCode.ToggleBitNotAlternated);
                    return;
                }
                // If we did not know the length up front (declared=0), grow lazily.
                if (session.Payload!.Length < session.Offset + payload.Length)
                {
                    var grown = new byte[session.Offset + payload.Length];
                    Buffer.BlockCopy(session.Payload, 0, grown, 0, session.Payload.Length);
                    session.Payload = grown;
                }
                Buffer.BlockCopy(payload, 0, session.Payload, session.Offset, payload.Length);
                session.Offset += payload.Length;
                session.Toggle = !session.Toggle;
                if (last)
                {
                    var final = session.Payload;
                    if (session.Offset != final.Length)
                    {
                        // Server declared a size but sent less. Trim.
                        var trimmed = new byte[session.Offset];
                        Buffer.BlockCopy(final, 0, trimmed, 0, session.Offset);
                        final = trimmed;
                    }
                    _sdoClients.Remove(serverNodeId);
                    session.Deadline?.Dispose();
                    session.Tcs.TrySetResult(final);
                    return;
                }
                SendNextClientUploadSegmentRequest(session);
                return;
            }
        }
    }

    private void SendNextClientDownloadSegment(SdoClientSession session)
    {
        int remaining = session.Payload!.Length - session.Offset;
        int chunk = Math.Min(7, remaining);
        var payload = new byte[chunk];
        Buffer.BlockCopy(session.Payload, session.Offset, payload, 0, chunk);
        bool last = (session.Offset + chunk) >= session.Payload.Length;
        session.Offset += chunk;
        var seg = SdoFrames.BuildSegment(SdoFrames.CcsDownloadSegmentBase, session.Toggle, last, payload);
        _ = SendControlFrame(CanOpenCobId.SdoRx(session.ServerNodeId), seg);
    }

    private void SendNextClientUploadSegmentRequest(SdoClientSession session)
    {
        byte cs = SdoFrames.CcsUploadSegmentBase;
        if (session.Toggle) cs |= SdoFrames.ToggleBit;
        var req = new byte[8];
        req[0] = cs;
        _ = SendControlFrame(CanOpenCobId.SdoRx(session.ServerNodeId), req);
    }

    private void AbortClient(SdoClientSession session, SdoAbortCode code)
    {
        _sdoClients.Remove(session.ServerNodeId);
        session.Deadline?.Dispose();
        _ = SendControlFrame(CanOpenCobId.SdoRx(session.ServerNodeId),
            SdoFrames.BuildAbort(session.Index, session.Subindex, (uint)code));
        session.Tcs.TrySetException(new SdoAbortException(session.Index, session.Subindex, code));
    }

    // =========================================================================================
    // PDO (FR-CO-005 / FR-CO-006)
    // =========================================================================================
    private void EmitTpdo(TpdoConfig config)
    {
        // Assemble the payload by concatenating the current OD values in mapping order. Each
        // mapping slot occupies its configured ByteLength window in the frame regardless of
        // whether the OD entry is present -- a missing entry leaves its window as the buffer's
        // default zero bytes and the next slot lands at its correct offset. The previous
        // behaviour skipped `offset += entry.ByteLength` on missing entries, which shifted
        // every subsequent value left of its configured byte position while total length
        // stayed the same, so consumers decoded the wrong fields even though the frame was
        // the "right size". See Bugbot 3600571636.
        var mapping = config.Mapping;
        var payload = new byte[mapping.TotalBytes];
        int offset = 0;
        foreach (var entry in mapping.Entries)
        {
            if (_od.TryGet(entry.Index, entry.Subindex, out var od))
            {
                var raw = od.GetRawValue();
                int copy = Math.Min(raw.Length, entry.ByteLength);
                Buffer.BlockCopy(raw, 0, payload, offset, copy);
            }
            offset += entry.ByteLength;
        }
        _ = SendControlFrame(config.CobId, payload);
    }

    private void HandleRpdo(RpdoConfig config, byte[] payload)
    {
        // Unpack into OD in mapping order (skip missing OD entries silently — mapping mismatch
        // is a config issue, not a protocol error). Route the write through
        // ObjectDictionary.WriteRaw so it happens under the OD's lock, giving readers on
        // other threads a proper release/acquire pairing.
        int offset = 0;
        foreach (var entry in config.Mapping.Entries)
        {
            if (offset + entry.ByteLength > payload.Length) break;
            if (_od.TryGet(entry.Index, entry.Subindex, out _))
            {
                var chunk = new byte[entry.ByteLength];
                Buffer.BlockCopy(payload, offset, chunk, 0, entry.ByteLength);
                try { _od.WriteRaw(entry.Index, entry.Subindex, chunk); }
                catch { /* ignore mapping/OD size mismatch */ }
            }
            offset += entry.ByteLength;
        }
        RaiseRpdoReceived(config.PdoIndex, config.CobId, payload);
    }

    private void ScheduleTpdoEventTimer(TpdoConfig config)
    {
        if (config.EventTimerInterval <= TimeSpan.Zero) return;
        config.EventTimerHandle = _actor.Schedule(config.EventTimerInterval, () =>
        {
            try
            {
                if (_disposed != 0) return;
                if (!_tpdos.TryGetValue(config.PdoIndex, out var current) || !ReferenceEquals(current, config))
                    return; // replaced or removed while we slept
                if (_state == NmtState.Operational)
                    EmitTpdo(config);
            }
            finally
            {
                if (_disposed == 0 && _tpdos.TryGetValue(config.PdoIndex, out var still)
                    && ReferenceEquals(still, config)
                    && config.Transmission == TpdoTransmission.EventTimer)
                {
                    ScheduleTpdoEventTimer(config);
                }
            }
        });
    }

    // =========================================================================================
    // Wire helpers
    // =========================================================================================
    private Task SendControlFrame(uint cobId, byte[] payload, CancellationToken cancellationToken = default)
    {
        // Classic 11-bit CAN frame; no extended bit. We do not await SendConfirmed for
        // fire-and-forget flows (SYNC / heartbeat producer / TPDO / EMCY / SDO) because their
        // callers do not need per-frame confirmation. For SDO client requests, an unconfirmed
        // send that would have thrown will still surface via BackgroundExceptionOccurred and
        // the SDO deadline will time the client out — the standard client contract.
        var frame = CanFrame.Classic(unchecked((int)cobId), payload, isExtendedFrame: false);
        return Task.Run(async () =>
        {
            try
            {
                var conf = await _service.SendConfirmed(frame, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!conf.Confirmed)
                {
                    RaiseBackgroundException(new CanOpenTransportException(
                        $"CANopen frame TX on COB-ID 0x{cobId:X3} failed: {conf.FailureReason}."));
                }
            }
            catch (OperationCanceledException) { /* caller-cancelled */ }
            catch (Exception ex)
            {
                RaiseBackgroundException(ex);
            }
        }, cancellationToken);
    }

    private void RaiseBackgroundException(Exception ex)
    {
        try { BackgroundExceptionOccurred?.Invoke(this, ex); }
        catch { /* subscriber must not tear down the node */ }
    }

    private void RaiseHeartbeatReceived(byte producer, NmtState state, DateTime ts)
    {
        var args = new HeartbeatReceivedEventArgs(producer, state, ts);
        EnqueueEvent(() =>
        {
            try { HeartbeatReceived?.Invoke(this, args); }
            catch (Exception ex) { RaiseBackgroundException(ex); }
        });
    }

    private void RaiseHeartbeatTimeout(byte producer, TimeSpan timeout)
    {
        var args = new HeartbeatTimeoutEventArgs(producer, timeout);
        EnqueueEvent(() =>
        {
            try { HeartbeatTimeout?.Invoke(this, args); }
            catch (Exception ex) { RaiseBackgroundException(ex); }
        });
    }

    private void RaiseEmcyReceived(EmcyMessage msg, DateTime ts)
    {
        var args = new EmcyReceivedEventArgs(msg, ts);
        EnqueueEvent(() =>
        {
            try { EmcyReceived?.Invoke(this, args); }
            catch (Exception ex) { RaiseBackgroundException(ex); }
        });
    }

    private void RaiseSyncReceived(DateTime ts)
    {
        var args = new SyncReceivedEventArgs(ts);
        EnqueueEvent(() =>
        {
            try { SyncReceived?.Invoke(this, args); }
            catch (Exception ex) { RaiseBackgroundException(ex); }
        });
    }

    private void RaiseRpdoReceived(int pdoIndex, uint cobId, byte[] payload)
    {
        var args = new RpdoReceivedEventArgs(pdoIndex, cobId, payload);
        EnqueueEvent(() =>
        {
            try { RpdoReceived?.Invoke(this, args); }
            catch (Exception ex) { RaiseBackgroundException(ex); }
        });
    }

    private void RaiseNmtCommandReceived(NmtCommand cmd, byte target)
    {
        var args = new NmtCommandReceivedEventArgs(cmd, target);
        EnqueueEvent(() =>
        {
            try { NmtCommandReceived?.Invoke(this, args); }
            catch (Exception ex) { RaiseBackgroundException(ex); }
        });
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(CanOpenNode));
    }

    // =========================================================================================
    // Nested state objects.
    // =========================================================================================

    private sealed class SdoServerSession
    {
        public SdoServerSession(bool inDownload, ushort index, byte subindex, byte[] buffer,
            int offset, bool toggle)
        {
            InDownload = inDownload;
            Index = index;
            Subindex = subindex;
            Buffer = buffer;
            Offset = offset;
            Toggle = toggle;
        }

        public bool InDownload { get; }
        public ushort Index { get; }
        public byte Subindex { get; }
        public byte[] Buffer { get; }
        public int Offset { get; set; }
        public bool Toggle { get; set; }
        public IDeadline? Deadline { get; set; }
    }

    private sealed class SdoClientSession
    {
        public SdoClientSession(byte serverNodeId, ushort index, byte subindex, bool isDownload,
            byte[]? payload, TaskCompletionSource<byte[]> tcs)
        {
            ServerNodeId = serverNodeId;
            Index = index;
            Subindex = subindex;
            IsDownload = isDownload;
            Payload = payload;
            Tcs = tcs;
        }

        public byte ServerNodeId { get; }
        public ushort Index { get; }
        public byte Subindex { get; }
        public bool IsDownload { get; }

        /// <summary>For download: the caller's data. For upload: filled during segmented
        /// transfer.</summary>
        public byte[]? Payload { get; set; }
        public int Offset { get; set; }
        public bool Toggle { get; set; }
        public TaskCompletionSource<byte[]> Tcs { get; }
        public IDeadline? Deadline { get; set; }
    }

    private sealed class HeartbeatConsumer
    {
        public HeartbeatConsumer(byte producerNodeId, TimeSpan timeout)
        {
            ProducerNodeId = producerNodeId;
            Timeout = timeout;
        }

        public byte ProducerNodeId { get; }
        public TimeSpan Timeout { get; }
        public IDeadline? Deadline { get; set; }
    }

    private sealed class TpdoConfig
    {
        public TpdoConfig(int pdoIndex, uint cobId, PdoMapping mapping,
            TpdoTransmission transmission, TimeSpan eventTimerInterval)
        {
            PdoIndex = pdoIndex;
            CobId = cobId;
            Mapping = mapping;
            Transmission = transmission;
            EventTimerInterval = eventTimerInterval;
        }

        public int PdoIndex { get; }
        public uint CobId { get; }
        public PdoMapping Mapping { get; }
        public TpdoTransmission Transmission { get; }
        public TimeSpan EventTimerInterval { get; }
        public IDisposable? EventTimerHandle { get; set; }
    }

    private sealed class RpdoConfig
    {
        public RpdoConfig(int pdoIndex, uint cobId, PdoMapping mapping)
        {
            PdoIndex = pdoIndex;
            CobId = cobId;
            Mapping = mapping;
        }

        public int PdoIndex { get; }
        public uint CobId { get; }
        public PdoMapping Mapping { get; }
    }
}
