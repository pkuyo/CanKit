using System;
using System.Collections.Generic;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;

namespace CanKit.Abstractions.SPI.Registry.Transports;

public sealed class IsoTpEndpointRegistration(
    string scheme,
    Func<CanEndpoint, IsoTpOptions, Action<IBusInitOptionsConfigurator>?, IIsoTpChannel> open,
    Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, PreparedBusContext> prepare)
{
    public string Scheme { get; init; } = scheme;
    public IReadOnlyList<string> Alias { get; init; } = [];
    public Func<CanEndpoint, IsoTpOptions, Action<IBusInitOptionsConfigurator>?, IIsoTpChannel> Open { get; init; } = open;
    public Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, PreparedBusContext> Prepare { get; init; } = prepare;
}

public interface IIsoTpRegister : ICanRegister
{
    IsoTpEndpointRegistration Endpoint { get; }
}
