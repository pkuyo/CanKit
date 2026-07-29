using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core.Registry;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Concurrency stress for <see cref="CanRegistry"/>: parallel RegisterEndPoint churn plus
/// concurrent readers (TryOpenEndPoint / EnumerateEndPoints) on the shared singleton must
/// not corrupt state or throw from racing dictionary enumeration.
/// </summary>
public class CanRegistryConcurrencyTests : IClassFixture<TestCaseProvider>
{
    // The internal Register* methods are reachable directly because CanKit.Core exposes
    // InternalsVisibleTo to this test assembly. The singleton (not a private instance) is
    // used deliberately, since the internal ctor mutates CanRegistry.Instance and would race
    // the lazy singleton build of other tests running in parallel.
    [Fact]
    public async Task CanRegistry_Parallel_Registration_And_Readers_Do_Not_Race()
    {
        var registry = CanRegistry.Registry;
        const int schemeCount = 16;
        var schemes = Enumerable.Range(0, schemeCount).Select(i => $"stress-{i}").ToArray();
        var exceptions = new ConcurrentQueue<Exception>();

        var stop = new ManualResetEventSlim();
        var readers = Enumerable.Range(0, 4).Select(_unused => Task.Run(() =>
        {
            try
            {
                while (!stop.IsSet)
                {
                    _ = registry.TryOpenEndPoint("stress-7://x", null, out _);
                    _ = registry.EnumerateEndPoints(null).Count();
                    _ = registry.EnumerateEndPoints(new[] { "stress-3" }).Count();
                }
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        })).ToArray();

        var writers = schemes.Select(scheme => Task.Run(() =>
        {
            try
            {
                registry.RegisterEndPoint(new RawEndpointRegistration(
                    scheme,
                    open: (ep, cfg) => null!,   // never invoked by this test's readers' assertion
                    prepare: (ep, cfg) => null!)
                {
                    Enumerate = () => Array.Empty<BusEndpointInfo>(),
                });
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        })).ToArray();

        try
        {
            await Task.WhenAll(writers);
            stop.Set();
            await Task.WhenAll(readers);
        }
        finally
        {
            // Remove the stress schemes under the registry's private lock so no residue is
            // visible to other tests sharing the singleton (and no unlocked mutation races
            // a concurrent snapshotting reader).
            WithRegistryLock(registry, () =>
            {
                foreach (var scheme in schemes)
                {
                    RemoveFromRegistryDictionaries(registry, scheme);
                }
            });
        }

        exceptions.Should().BeEmpty("parallel registration and reads must be race-free");

        // Spot-check consistency: re-register one scheme and resolve it through the public API.
        var probe = "stress-probe";
        try
        {
            registry.RegisterEndPoint(new RawEndpointRegistration(
                probe, open: (ep, cfg) => null!, prepare: (ep, cfg) => null!));
            registry.TryOpenEndPoint($"{probe}://x", null, out _).Should().BeTrue(
                "a scheme registered under concurrency must remain resolvable afterwards");
        }
        finally
        {
            WithRegistryLock(registry, () => RemoveFromRegistryDictionaries(registry, probe));
        }
    }

    private static void WithRegistryLock(CanRegistry registry, Action action)
    {
        var syncField = typeof(CanRegistry).GetField("_sync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CanRegistry._sync field not found.");
        lock (syncField.GetValue(registry)!)
        {
            action();
        }
    }

    private static void RemoveFromRegistryDictionaries(CanRegistry registry, string scheme)
    {
        var type = typeof(CanRegistry);
        foreach (var fieldName in new[] { "_handlers", "_prepareHandlers", "_enumerators", "_enumeratorAlias" })
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"CanRegistry.{fieldName} field not found.");
            ((IDictionary)field.GetValue(registry)!).Remove(scheme);
        }
    }
}
