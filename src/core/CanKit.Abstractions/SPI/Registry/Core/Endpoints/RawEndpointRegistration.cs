using System;
using System.Collections.Generic;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;

namespace CanKit.Abstractions.SPI.Registry.Core.Endpoints;

public sealed class RawEndpointRegistration(
    string scheme,
    Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, ICanBus> open,
    Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, PreparedBusContext> prepare)
{
    public string Scheme { get; init; } = scheme;
    public IReadOnlyList<string> Alias { get; init; } = [];
    public Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, ICanBus> Open { get; init; } = open;
    public Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, PreparedBusContext> Prepare { get; init; } = prepare;

    public Func<IEnumerable<BusEndpointInfo>>? Enumerate { get; init; }
}
