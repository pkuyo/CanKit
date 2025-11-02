using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Core.Diagnostics;

namespace CanKit.Core.Registry;

public partial class CanRegistry
{

    private static IEnumerable<(int Order, ICanRegister Register, string Desc)> DiscoverRegisters(Assembly asm)
    {
        foreach (var type in SafeGetTypes(asm))
        {
            var attr = type.GetCustomAttribute<CanRegistryEntryAttribute>(inherit: false);
            if (attr?.Enabled == true && typeof(ICanRegister).IsAssignableFrom(type)
                && !type.IsAbstract && !type.IsGenericTypeDefinition)
            {
                ICanRegister? reg = null;
                try { reg = (ICanRegister?)Activator.CreateInstance(type); }
                catch { /* ignore creation failure */ }
                if (reg is null) continue;
                var name = attr.Name;
                yield return (attr.Order, reg, name);
            }
        }
    }

    private static IEnumerable<(int Order, ICanRegistryEntry Entry, string Desc)> DiscoverEntries(Assembly asm)
    {
        foreach (var type in SafeGetTypes(asm))
        {
            var attr = type.GetCustomAttribute<CanRegistryEntryAttribute>(inherit: false);
            if (attr?.Enabled == true && typeof(ICanRegistryEntry).IsAssignableFrom(type)
                && !type.IsAbstract && !type.IsGenericTypeDefinition)
            {
                ICanRegistryEntry? entry = null;
                try { entry = (ICanRegistryEntry?)Activator.CreateInstance(type); }
                catch { /* ignore creation failure */ }
                if (entry is null) continue;
                var name = attr.Name ?? type.FullName ?? type.Name;
                yield return (attr.Order, entry, name);
            }
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
        catch { return []; }
    }

    private void ExecuteRegistrationPipeline(Assembly[] assemblies)
    {
        var registers = new List<(int Order, ICanRegister Register, string Desc)>();
        var entries = new List<(int Order, ICanRegistryEntry Entry, string Desc)>();

        foreach (var asm in assemblies)
        {
            // Collect adapter-provided registers
            registers.AddRange(DiscoverRegisters(asm));

            // Collect entry implementations
            entries.AddRange(DiscoverEntries(asm));
        }

        // Execute: for each register, run all entries by order
        foreach (var (_, reg, rName) in registers.OrderBy(r => r.Order))
        {
            foreach (var (order, entry, eDesc) in entries.OrderBy(e => e.Order))
            {
                try { entry.Register(rName, reg); }
                catch (Exception ex)
                {
                    CanKitLogger.LogWarning($"Entry '{eDesc}' failed for Register '{rName}'. Order={order}", ex);
                }
            }
        }
    }
}
