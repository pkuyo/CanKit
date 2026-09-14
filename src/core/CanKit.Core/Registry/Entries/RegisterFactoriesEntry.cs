using System;
using CanKit.Abstractions.SPI.Registry.Core;

namespace CanKit.Core.Registry.Entries;

internal sealed class RegisterFactoriesEntry : ICanRegistryEntry
{
    public void Register(string name, ICanRegister register)
    {
        if (register is not ICanRegisterFactory fr) return;
        var (id, factory) = fr.Factory;
        if (string.IsNullOrWhiteSpace(id)) return;
        try
        {
            CanRegistry.Instance!.RegisterFactory(id, factory);
        }
        catch (Exception ex)
        {
            Diagnostics.CanKitLogger.LogWarning($"Factory registration failed. Id='{id}', Register='{name}'", ex);
        }

    }
}

