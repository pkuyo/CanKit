using System;
using CanKit.Abstractions.API.Can;
using CanKit.Pro.RawCan;

namespace CanKit.Pro.CANopen;

/// <summary>
/// Factory entry point for opening <see cref="ICanOpenNode"/> instances. Non-instantiable.
/// </summary>
/// <remarks>
/// Mirrors the ownership patterns used by <c>CanKit.Pro.IsoTp</c>, <c>CanKit.Pro.J1939Tp</c> and
/// <c>CanKit.Pro.Uds</c>:
/// <list type="bullet">
///   <item><description><see cref="OpenNode(ICanBus, byte, CanOpenNodeOptions?)"/> — the node
///   owns a private <see cref="ICanBusService"/> wrapping the supplied bus and disposes it on
///   dispose. Convenient for single-protocol callers.</description></item>
///   <item><description><see cref="OpenNode(ICanBusService, byte, CanOpenNodeOptions?, bool)"/> —
///   the caller supplies an existing service (shared with other protocols on the same bus per
///   FR-CO-012); with <c>leaveOpen=true</c> the service outlives the node.</description></item>
/// </list>
/// </remarks>
public static class CanOpen
{
    /// <summary>Opens a node that owns a private <see cref="CanBusService"/> around
    /// <paramref name="bus"/>. Disposing the node disposes that service and detaches from
    /// <paramref name="bus"/>; the bus itself is not disposed.</summary>
    public static ICanOpenNode OpenNode(ICanBus bus, byte nodeId, CanOpenNodeOptions? options = null)
    {
        if (bus is null) throw new ArgumentNullException(nameof(bus));
        var service = new CanBusService(bus);
        try
        {
            return new CanOpenNode(service, nodeId, options ?? new CanOpenNodeOptions(),
                ownsService: true);
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    /// <summary>Opens a node bound to an already-existing <paramref name="service"/>. Multiple
    /// nodes with different node-ids may share the same service to multiplex several CANopen
    /// identities over one physical bus, or to co-exist with other protocol layers (ISO-TP,
    /// J1939, ...).</summary>
    /// <param name="service">The demux service. Must not be null.</param>
    /// <param name="nodeId">CANopen node-id 1..127 this node identifies as.</param>
    /// <param name="options">Node options; defaults to a fresh <see cref="CanOpenNodeOptions"/>.</param>
    /// <param name="leaveOpen">When <c>true</c> (default) disposing the node does not dispose
    /// <paramref name="service"/>; when <c>false</c> the node takes ownership and disposes the
    /// service on its own <see cref="IDisposable.Dispose"/>.</param>
    public static ICanOpenNode OpenNode(ICanBusService service, byte nodeId,
        CanOpenNodeOptions? options = null, bool leaveOpen = true)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        return new CanOpenNode(service, nodeId, options ?? new CanOpenNodeOptions(),
            ownsService: !leaveOpen);
    }
}
