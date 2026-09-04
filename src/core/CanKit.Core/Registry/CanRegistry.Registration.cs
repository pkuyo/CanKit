using System;
using System.Collections.Generic;
using System.Linq;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Core.Diagnostics;
using CanKit.Core.Registry.Entries;

namespace CanKit.Core.Registry;

public partial class CanRegistry
{
    private void ExecuteRegistrationPipeline(RegistrationSnapshot snapshot)
    {
        var registers = new List<(int Order, ICanRegister Register, string Name)>();
        foreach (var registration in snapshot.Adapters)
        {
            try
            {
                registers.Add((registration.Order, registration.Factory(), registration.Name));
            }
            catch (Exception ex)
            {
                CanKitLogger.LogWarning($"Failed to create adapter registration '{registration.Name}'.", ex);
            }
        }

        var entries = new List<(int Order, ICanRegistryEntry Entry, string Name)>
        {
            (-100, new RegisterFactoriesEntry(), "Factories"),
            (-50, new RegisterProvidersEntry(), "Providers"),
            (0, new RegisterEndpointsEntry(), "Endpoints")
        };

        foreach (var registration in snapshot.Extensions)
        {
            try
            {
                entries.Add((registration.Order, registration.Factory(), registration.Name));
            }
            catch (Exception ex)
            {
                CanKitLogger.LogWarning($"Failed to create extension registration '{registration.Name}'.", ex);
            }
        }

        var orderedEntries = entries.OrderBy(entry => entry.Order).ToArray();
        foreach (var (_, register, registerName) in registers.OrderBy(register => register.Order))
        {
            foreach (var (order, entry, entryName) in orderedEntries)
            {
                try
                {
                    entry.Register(registerName, register);
                }
                catch (Exception ex)
                {
                    CanKitLogger.LogWarning(
                        $"Entry '{entryName}' failed for Register '{registerName}'. Order={order}", ex);
                }
            }
        }
    }
}
