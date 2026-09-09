using System;
using CanKit.Abstractions.SPI.Registry.Core;

namespace CanKit.Core.Registry.Entries;

internal sealed class RegisterEndpointsEntry : ICanRegistryEntry
{
    public void Register(string name, ICanRegister register)
    {
        if (register is not IRawRegisterEndpoint er) return;
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

