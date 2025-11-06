using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Abstractions.SPI.Registry.Transports;
using CanKit.Core.Registry;

namespace CanKit.Transport.IsoTp.Registry;

public class IsoTpRegistry
{
    private static readonly object _pendingLock = new();
    private static readonly List<IsoTpEndpointRegistration> _pending = new();

    public static Lazy<IsoTpRegistry> Registry { get; } = new(() => new IsoTpRegistry());

    public bool TryOpenEndPoint(string endpoint, IsoTpOptions option, Action<IBusInitOptionsConfigurator>? configure, out IIsoTpChannel? channel)
    {
        var ep = CanEndpoint.Parse(endpoint);
        if (_handlers.TryGetValue(ep.Scheme, out var h))
        {
            channel = h(ep, option, configure);
            return true;
        }
        channel = null;
        return false;
    }

    public bool TryPrepareEndPoint(string endpoint, Action<IBusInitOptionsConfigurator>? configure, out PreparedBusContext? prepared)
    {
        var ep = CanEndpoint.Parse(endpoint);
        if (_prepareHandlers.TryGetValue(ep.Scheme, out var p))
        {
            prepared = p(ep, configure);
            return true;
        }
        prepared = null;
        return false;
    }

    private IsoTpRegistry()
    {
        _ = CanRegistry.Registry;
        lock (_pendingLock)
        {
            if (_pending.Count > 0)
            {
                foreach (var ep in _pending)
                {
                    RegisterEndPoint(ep);
                }
                _pending.Clear();
            }
        }
    }

    internal void RegisterEndPoint(IsoTpEndpointRegistration endpoint)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        if (string.IsNullOrWhiteSpace(endpoint.Scheme)) throw new ArgumentNullException(nameof(endpoint.Scheme));

        _handlers[endpoint.Scheme] = endpoint.Open;
        _prepareHandlers[endpoint.Scheme] = endpoint.Prepare;

        foreach (var alias in endpoint.Alias.Append(endpoint.Scheme))
        {
            if (!_enumeratorAlias.ContainsKey(alias))
                _enumeratorAlias[alias] = endpoint.Scheme;
        }
    }

    internal static void Enqueue(IsoTpEndpointRegistration endpoint)
    {
        if (Registry.IsValueCreated)
        {
            Registry.Value.RegisterEndPoint(endpoint);
            return;
        }
        lock (_pendingLock)
        {
            _pending.Add(endpoint);
        }
    }

    private readonly Dictionary<string, string> _enumeratorAlias = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Func<CanEndpoint, IsoTpOptions, Action<IBusInitOptionsConfigurator>?, IIsoTpChannel>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, PreparedBusContext>> _prepareHandlers =
        new(StringComparer.OrdinalIgnoreCase);
}
