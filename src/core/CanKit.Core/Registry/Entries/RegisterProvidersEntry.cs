using System;
using System.Linq;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Registry.Core;

namespace CanKit.Core.Registry.Entries;

[CanRegistryEntry(CanRegistryEntryKind.Adapter, "Providers", Order = -50)]
internal sealed class RegisterProvidersEntry : ICanRegistryEntry
{
    public void Register(string name, ICanRegister register)
    {
        if (register is not ICanRegisterProviders pr) return;
        var providers = pr.Providers.ToArray();
        if (providers.Length == 0) return;
        try { CanRegistry.Instance!.RegisterProvider(providers!); }
        catch (Exception ex)
        {
            Diagnostics.CanKitLogger.LogWarning($"Provider registration failed. Register='{name}'", ex);
        }
    }
}

