using System;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Pro.CANopen.Emcy;
using CanKit.Pro.CANopen.Nmt;
using CanKit.Pro.CANopen.Pdo;

namespace CanKit.Pro.CANopen;

/// <summary>
/// A single CANopen node running on top of the CanKit.Pro L2 demux (arc42 §5.3, ADR-5;
/// FR-CO-012). Combines an NMT slave state machine, a local Object Dictionary, an SDO server
/// (for the local OD) and an SDO client (for remote nodes on the same bus), plus the SYNC,
/// EMCY, heartbeat producer/consumer and PDO plumbing needed by CiA 301 §7.
/// </summary>
/// <remarks>
/// One instance represents one CANopen node identity (1..127) on one physical bus. Two or more
/// nodes may share the same underlying <see cref="CanKit.Pro.RawCan.ICanBusService"/> so a
/// process-hosted "master" and one or more simulated "slaves" can coexist on a virtual bus in
/// tests — that is what the MVP integration tests exercise.
/// </remarks>
public interface ICanOpenNode : IDisposable
{
    /// <summary>Node identifier (1..127) this instance answers as on the bus.</summary>
    byte NodeId { get; }

    /// <summary>Options this node was constructed with.</summary>
    CanOpenNodeOptions Options { get; }

    /// <summary>The local Object Dictionary (FR-CO-001). Shared by SDO server, PDO mapping and
    /// application code.</summary>
    ObjectDictionary ObjectDictionary { get; }

    /// <summary>Current NMT slave state of this node.</summary>
    NmtState State { get; }

    // -----------------------------------------------------------------------------------------
    // Events
    // -----------------------------------------------------------------------------------------

    /// <summary>Raised when a heartbeat (or bootup) frame is received for any node the local
    /// consumer subscribed to.</summary>
    event EventHandler<HeartbeatReceivedEventArgs>? HeartbeatReceived;

    /// <summary>Raised when a configured heartbeat consumer detects a missing heartbeat.</summary>
    event EventHandler<HeartbeatTimeoutEventArgs>? HeartbeatTimeout;

    /// <summary>Raised when an EMCY frame is received on the bus (FR-CO-011).</summary>
    event EventHandler<EmcyReceivedEventArgs>? EmcyReceived;

    /// <summary>Raised whenever a SYNC frame is received (either from a remote producer or from
    /// this node's own producer if echo is on).</summary>
    event EventHandler<SyncReceivedEventArgs>? SyncReceived;

    /// <summary>Raised after an RPDO the local node has mapped is received and unpacked into
    /// the OD.</summary>
    event EventHandler<RpdoReceivedEventArgs>? RpdoReceived;

    /// <summary>Raised on the actor loop for every NMT master command whose target matches this
    /// node (or the broadcast target 0).</summary>
    event EventHandler<NmtCommandReceivedEventArgs>? NmtCommandReceived;

    /// <summary>Raised on background exceptions from the actor loop / subscription reader.</summary>
    event EventHandler<Exception>? BackgroundExceptionOccurred;

    // -----------------------------------------------------------------------------------------
    // NMT master (FR-CO-007)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Sends an NMT master command (COB-ID <c>0x000</c>). <paramref name="targetNodeId"/>
    /// zero means broadcast to all nodes. Applying the command to the local node (either
    /// broadcast or targeting this node's own id) is expected to be handled by the receiver
    /// via <see cref="NmtCommandReceived"/>.
    /// </summary>
    Task SendNmtCommandAsync(NmtCommand command, byte targetNodeId,
        CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------------------------
    // Heartbeat producer / consumer (FR-CO-008)
    // -----------------------------------------------------------------------------------------

    /// <summary>Starts (or replaces) the local heartbeat producer with
    /// <paramref name="interval"/>.</summary>
    void StartHeartbeatProducer(TimeSpan interval);

    /// <summary>Stops the local heartbeat producer.</summary>
    void StopHeartbeatProducer();

    /// <summary>Registers (or replaces) a heartbeat consumer for
    /// <paramref name="producerNodeId"/>: if no heartbeat is received within
    /// <paramref name="timeout"/>, the node raises <see cref="HeartbeatTimeout"/>.</summary>
    void AddHeartbeatConsumer(byte producerNodeId, TimeSpan timeout);

    /// <summary>Removes a previously-registered heartbeat consumer for
    /// <paramref name="producerNodeId"/>. No-op if none was registered.</summary>
    void RemoveHeartbeatConsumer(byte producerNodeId);

    // -----------------------------------------------------------------------------------------
    // SYNC (FR-CO-010)
    // -----------------------------------------------------------------------------------------

    /// <summary>Starts a periodic SYNC producer with the given interval.</summary>
    void StartSyncProducer(TimeSpan interval);

    /// <summary>Stops the periodic SYNC producer.</summary>
    void StopSyncProducer();

    /// <summary>Transmits a single SYNC frame (payload-less).</summary>
    Task SendSyncAsync(CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------------------------
    // EMCY (FR-CO-011)
    // -----------------------------------------------------------------------------------------

    /// <summary>Transmits an EMCY frame from this node.</summary>
    Task SendEmcyAsync(ushort errorCode, byte errorRegister,
        ReadOnlyMemory<byte> manufacturerSpecific = default,
        CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------------------------
    // SDO client (FR-CO-002 / FR-CO-003)
    // -----------------------------------------------------------------------------------------

    /// <summary>Reads an object from <paramref name="serverNodeId"/>'s OD using SDO expedited
    /// or segmented upload, depending on the entry size.</summary>
    Task<byte[]> SdoUploadAsync(byte serverNodeId, ushort index, byte subindex,
        CancellationToken cancellationToken = default);

    /// <summary>Writes an object to <paramref name="serverNodeId"/>'s OD using SDO expedited or
    /// segmented download, depending on the payload size.</summary>
    Task SdoDownloadAsync(byte serverNodeId, ushort index, byte subindex,
        ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------------------------
    // PDO (FR-CO-005 / FR-CO-006)
    // -----------------------------------------------------------------------------------------

    /// <summary>Configures (or replaces) TPDO <paramref name="pdoIndex"/> (1..4). Values not
    /// provided use CiA 301 defaults: COB-ID is the pre-defined connection set entry and the
    /// event timer defaults to <see cref="CanOpenNodeOptions.DefaultTpdoEventTimerInterval"/>.
    /// </summary>
    void ConfigureTpdo(int pdoIndex, PdoMapping mapping,
        TpdoTransmission transmission = TpdoTransmission.EventDriven,
        uint? cobId = null,
        TimeSpan? eventTimerInterval = null);

    /// <summary>Configures (or replaces) RPDO <paramref name="pdoIndex"/> (1..4). Incoming
    /// frames matching the mapping unpack straight into the local OD and raise
    /// <see cref="RpdoReceived"/>.</summary>
    void ConfigureRpdo(int pdoIndex, PdoMapping mapping, uint? cobId = null);

    /// <summary>Manually triggers a TPDO transmission. The node still respects the current
    /// NMT state (only fires when the node is in <see cref="NmtState.Operational"/>).</summary>
    Task TriggerTpdoAsync(int pdoIndex, CancellationToken cancellationToken = default);
}
