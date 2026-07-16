using System;
using CanKit.Abstractions.API.Can;
using CanKit.Pro.RawCan;

namespace CanKit.Pro.J1939Tp;

/// <summary>
/// Factory entry point for opening <see cref="IJ1939TpChannel"/> instances. Non-instantiable.
/// </summary>
/// <remarks>
/// Two overloads reflect the two ownership patterns, mirroring the ISO-TP factory:
/// <list type="bullet">
///   <item><description><see cref="Open(ICanBus, byte, J1939TpOptions?)"/> — the channel owns a
///   private <see cref="ICanBusService"/> that wraps the supplied bus and disposes it when the
///   channel is disposed. Convenient for single-protocol callers.</description></item>
///   <item><description><see cref="Open(ICanBusService, byte, J1939TpOptions?, bool)"/> — the
///   caller supplies an already-existing service (e.g. shared with an ISO-TP or CANopen
///   instance on the same bus per FR-TP-018/034); with <c>leaveOpen=true</c> (the default) the
///   service outlives the channel.</description></item>
/// </list>
/// </remarks>
public static class J1939Tp
{
    /// <summary>
    /// Opens a channel that owns a private <see cref="CanBusService"/> around <paramref name="bus"/>.
    /// Disposing the returned channel disposes that service and detaches from
    /// <paramref name="bus"/>; the bus itself is not disposed.
    /// </summary>
    public static IJ1939TpChannel Open(
        ICanBus bus,
        byte sourceAddress,
        J1939TpOptions? options = null)
    {
        if (bus is null) throw new ArgumentNullException(nameof(bus));
        var service = new CanBusService(bus);
        try
        {
            return new J1939TpChannel(service, sourceAddress, options ?? new J1939TpOptions(),
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
    /// with different source addresses may share the same service to multiplex several J1939
    /// nodes over one physical bus, or to co-exist with other protocol layers (ISO-TP,
    /// CANopen, ...).
    /// </summary>
    /// <param name="service">The demux service. Must not be null.</param>
    /// <param name="sourceAddress">The J1939 source address this channel identifies as.</param>
    /// <param name="options">Channel options; defaults to a fresh <see cref="J1939TpOptions"/>.</param>
    /// <param name="leaveOpen">When <c>true</c> (default) disposing the channel does not dispose
    /// <paramref name="service"/>; the caller retains ownership. When <c>false</c> the channel
    /// takes ownership and disposes the service on its own <see cref="IDisposable.Dispose"/>.</param>
    public static IJ1939TpChannel Open(
        ICanBusService service,
        byte sourceAddress,
        J1939TpOptions? options = null,
        bool leaveOpen = true)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        return new J1939TpChannel(service, sourceAddress, options ?? new J1939TpOptions(),
            ownsService: !leaveOpen);
    }
}
