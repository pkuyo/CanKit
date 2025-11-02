using System;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Registry.Core;

namespace CanKit.Core.Registry.Entries;

[CanRegistryEntry(CanRegistryEntryKind.Adapter, "Endpoints", Order = 0)]
internal sealed class RegisterEndpointsEntry : ICanRegistryEntry
{
    public void Register(string name, ICanRegister register)
    {
        if (register is not ICanRegisterEndpoint er) return;
        var endpoint = er.Endpoint;
        try
        {
            CanRegistry.Instance!.RegisterEndPoint(endpoint);
        }
        catch (Exception ex)
        {
            Diagnostics.CanKitLogger.LogWarning($"Endpoint registration failed. Scheme='{endpoint.Scheme}', Register='{name}'", ex);
        }
    }
}

