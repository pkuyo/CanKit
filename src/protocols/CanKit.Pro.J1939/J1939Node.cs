using System;
using CanKit.Abstractions.API.Can;
using CanKit.Pro.RawCan;

namespace CanKit.Pro.J1939;

/// <summary>
/// Factory entry point for opening <see cref="IJ1939Node"/> instances. Non-instantiable.
/// </summary>
/// <remarks>
/// Two overloads reflect the two ownership patterns, mirroring the J1939-TP / UDS factories:
/// <list type="bullet">
///   <item><description><see cref="Open(ICanBus, J1939NodeOptions)"/> — the node owns a
///   private <see cref="ICanBusService"/> around the supplied <see cref="ICanBus"/> and
///   disposes it on <see cref="IDisposable.Dispose"/>. Convenient for single-protocol
///   callers.</description></item>
///   <item><description><see cref="Open(ICanBusService, J1939NodeOptions, bool)"/> — the
///   caller supplies an already-existing service (potentially shared with an ISO-TP / UDS /
///   J1939-TP instance on the same bus); with <c>leaveOpen=true</c> (the default) the service
///   outlives the node.</description></item>
/// </list>
/// </remarks>
public static class J1939Node
{
    /// <summary>
    /// Opens a node that owns a private <see cref="CanBusService"/> around <paramref name="bus"/>.
    /// Disposing the returned node disposes that service and detaches from <paramref name="bus"/>;
    /// the bus itself is not disposed.
    /// </summary>
    public static IJ1939Node Open(ICanBus bus, J1939NodeOptions options)
    {
        if (bus is null) throw new ArgumentNullException(nameof(bus));
        if (options is null) throw new ArgumentNullException(nameof(options));
        var service = new CanBusService(bus);
        try
        {
            return new J1939NodeImpl(service, options, ownsService: true);
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a node bound to an already-existing <paramref name="service"/>. Multiple nodes
    /// with different <see cref="J1939NodeOptions.Name"/> identities may share the same
    /// service to multiplex several J1939 nodes over one physical bus, or to co-exist with
    /// other protocol layers.
    /// </summary>
    /// <param name="service">The demux service. Must not be null.</param>
    /// <param name="options">Node options; must not be null.</param>
    /// <param name="leaveOpen">When <c>true</c> (default) disposing the node does not dispose
    /// <paramref name="service"/>; the caller retains ownership.</param>
    public static IJ1939Node Open(ICanBusService service, J1939NodeOptions options, bool leaveOpen = true)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        if (options is null) throw new ArgumentNullException(nameof(options));
        return new J1939NodeImpl(service, options, ownsService: !leaveOpen);
    }
}
