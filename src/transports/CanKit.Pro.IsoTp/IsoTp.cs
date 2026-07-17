using System;
using CanKit.Abstractions.API.Can;
using CanKit.Pro.RawCan;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// Factory entry point for opening <see cref="IIsoTpChannel"/> and
/// <see cref="IsoTpFunctionalClient"/> instances. Non-instantiable.
/// </summary>
/// <remarks>
/// Physical (1:1) channel overloads reflect two ownership patterns:
/// <list type="bullet">
/// <item><description><see cref="Open(ICanBus, IsoTpEndpoint, IsoTpChannelOptions?)"/> — the
/// channel owns a private <see cref="ICanBusService"/> that wraps the supplied bus and disposes
/// it when the channel is disposed. Convenient for single-protocol callers.</description></item>
/// <item><description><see cref="Open(ICanBusService, IsoTpEndpoint, IsoTpChannelOptions?, bool)"/>
/// — the caller supplies an already-existing service (e.g. shared by multiple protocol instances
/// on the same bus per FR-TP-018); with <c>leaveOpen=true</c> (the default) the service outlives
/// the channel.</description></item>
/// </list>
/// Functional (1:N) overloads open an <see cref="IsoTpFunctionalClient"/> for broadcast
/// requests; see <see cref="OpenFunctional(ICanBus, uint, uint, uint, IsoTpFunctionalOptions?)"/>
/// and <see cref="OpenFunctional(ICanBusService, uint, uint, uint, IsoTpFunctionalOptions?, bool)"/>.
/// </remarks>
public static class IsoTp
{
    // -----------------------------------------------------------------------------------------
    // Physical (1:1) channel factory
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Opens a channel that owns a private <see cref="CanBusService"/> around <paramref name="bus"/>.
    /// Disposing the returned channel disposes that service and detaches from
    /// <paramref name="bus"/>; the bus itself is not disposed.
    /// </summary>
    public static IIsoTpChannel Open(
        ICanBus bus,
        IsoTpEndpoint endpoint,
        IsoTpChannelOptions? options = null)
    {
        if (bus is null) throw new ArgumentNullException(nameof(bus));
        var service = new CanBusService(bus);
        try
        {
            return new IsoTpChannel(service, endpoint, options ?? new IsoTpChannelOptions(),
                ownsService: true);
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a channel bound to an already-existing <paramref name="service"/>. Multiple channels
    /// with disjoint endpoints may share the same service to multiplex several ISO-TP sessions
    /// over one physical bus (FR-TP-018).
    /// </summary>
    /// <param name="service">The demux service. Must not be null.</param>
    /// <param name="endpoint">Endpoint the channel is bound to.</param>
    /// <param name="options">Channel options; defaults to a fresh <see cref="IsoTpChannelOptions"/>.</param>
    /// <param name="leaveOpen">When <c>true</c> (default) disposing the channel does not dispose
    /// <paramref name="service"/>; the caller retains ownership. When <c>false</c> the channel
    /// takes ownership and disposes the service on its own <see cref="IDisposable.Dispose"/>.</param>
    public static IIsoTpChannel Open(
        ICanBusService service,
        IsoTpEndpoint endpoint,
        IsoTpChannelOptions? options = null,
        bool leaveOpen = true)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        return new IsoTpChannel(service, endpoint, options ?? new IsoTpChannelOptions(),
            ownsService: !leaveOpen);
    }

    // -----------------------------------------------------------------------------------------
    // Functional (1:N) client factory — FR-TP-019
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Opens a functional (1:N broadcast) client that owns a private <see cref="CanBusService"/>
    /// around <paramref name="bus"/>.
    /// </summary>
    /// <param name="bus">The CAN bus to attach to. Must not be null.</param>
    /// <param name="functionalTxCanId">
    /// The shared functional/broadcast CAN identifier used for outbound Single Frames
    /// (e.g. <c>0x7DF</c> for UDS 11-bit functional addressing).
    /// </param>
    /// <param name="responseRxCanIdRangeStart">
    /// Inclusive start of the CAN-ID range on which ECU responses are expected
    /// (e.g. <c>0x7E8</c>).
    /// </param>
    /// <param name="responseRxCanIdRangeEnd">
    /// Inclusive end of the CAN-ID range on which ECU responses are expected
    /// (e.g. <c>0x7EF</c>). Must be ≥ <paramref name="responseRxCanIdRangeStart"/>.
    /// </param>
    /// <param name="options">
    /// Functional client options; defaults to a fresh <see cref="IsoTpFunctionalOptions"/>.
    /// </param>
    public static IsoTpFunctionalClient OpenFunctional(
        ICanBus bus,
        uint functionalTxCanId,
        uint responseRxCanIdRangeStart,
        uint responseRxCanIdRangeEnd,
        IsoTpFunctionalOptions? options = null)
    {
        if (bus is null) throw new ArgumentNullException(nameof(bus));
        var service = new CanBusService(bus);
        try
        {
            return new IsoTpFunctionalClient(service, functionalTxCanId,
                responseRxCanIdRangeStart, responseRxCanIdRangeEnd,
                options ?? new IsoTpFunctionalOptions(), ownsService: true);
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a functional (1:N broadcast) client bound to an already-existing
    /// <paramref name="service"/>. The service may be shared with physical
    /// <see cref="IIsoTpChannel"/> instances on the same bus as long as the functional
    /// response range and any physical <see cref="IsoTpEndpoint.RxCanId"/> values are disjoint
    /// (FR-TP-018).
    /// </summary>
    /// <param name="service">The demux service. Must not be null.</param>
    /// <param name="functionalTxCanId">Functional/broadcast TX CAN identifier.</param>
    /// <param name="responseRxCanIdRangeStart">Inclusive start of the ECU response CAN-ID range.</param>
    /// <param name="responseRxCanIdRangeEnd">
    /// Inclusive end of the ECU response CAN-ID range. Must be ≥
    /// <paramref name="responseRxCanIdRangeStart"/>.
    /// </param>
    /// <param name="options">Functional client options; defaults to a fresh <see cref="IsoTpFunctionalOptions"/>.</param>
    /// <param name="leaveOpen">
    /// When <c>true</c> (default) disposing the client does not dispose <paramref name="service"/>.
    /// When <c>false</c> the client takes ownership and disposes the service on its own
    /// <see cref="IDisposable.Dispose"/>.
    /// </param>
    public static IsoTpFunctionalClient OpenFunctional(
        ICanBusService service,
        uint functionalTxCanId,
        uint responseRxCanIdRangeStart,
        uint responseRxCanIdRangeEnd,
        IsoTpFunctionalOptions? options = null,
        bool leaveOpen = true)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        return new IsoTpFunctionalClient(service, functionalTxCanId,
            responseRxCanIdRangeStart, responseRxCanIdRangeEnd,
            options ?? new IsoTpFunctionalOptions(), ownsService: !leaveOpen);
    }
}
