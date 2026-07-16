using System;
using CanKit.Abstractions.API.Can;
using CanKit.Pro.RawCan;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// Factory entry point for opening <see cref="IIsoTpChannel"/> instances. Non-instantiable.
/// </summary>
/// <remarks>
/// Two overloads reflect the two ownership patterns:
/// <list type="bullet">
/// <item><description><see cref="Open(ICanBus, IsoTpEndpoint, IsoTpChannelOptions?)"/> — the
/// channel owns a private <see cref="ICanBusService"/> that wraps the supplied bus and disposes
/// it when the channel is disposed. Convenient for single-protocol callers.</description></item>
/// <item><description><see cref="Open(ICanBusService, IsoTpEndpoint, IsoTpChannelOptions?, bool)"/>
/// — the caller supplies an already-existing service (e.g. shared by multiple protocol instances
/// on the same bus per FR-TP-018); with <c>leaveOpen=true</c> (the default) the service outlives
/// the channel.</description></item>
/// </list>
/// </remarks>
public static class IsoTp
{
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
}
