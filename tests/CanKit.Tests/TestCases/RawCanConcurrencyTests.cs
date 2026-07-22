using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Core;
using CanKit.Core.Registry;
using CanKit.Pro.RawCan;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// NFR-008 concurrency stress: the L2 components shared by many protocol instances at once —
/// the endpoint registry and the RawCan demultiplex service — must be race-free under
/// parallel registration / subscription churn while readers and traffic keep flowing.
/// Repeated enough times to give races a chance, bounded enough for CI (~a few seconds).
/// </summary>
public class RawCanConcurrencyTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(60);

    private static string NewSession() => $"rawcan-stress-{Guid.NewGuid():N}";

    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    // NFR-008 (registry): parallel RegisterEndPoint calls (the internal late-registration
    // surface used by the SPI pipeline) plus concurrent public readers (TryOpenEndPoint /
    // EnumerateEndPoints) on the shared singleton must not corrupt state or throw
    // InvalidOperationException from racing dictionary enumeration. The internal Register*
    // methods are reachable directly because CanKit.Core exposes InternalsVisibleTo to this
    // test assembly; the singleton (not a private instance) is used deliberately, since the
    // internal ctor mutates CanRegistry.Instance and would race the lazy singleton build of
    // other tests running in parallel.
    [Fact]
    public void CanRegistry_Parallel_Registration_And_Readers_Do_Not_Race()
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
            Task.WaitAll(writers);
            stop.Set();
            Task.WaitAll(readers);
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

        exceptions.Should().BeEmpty("parallel registration and reads must be race-free (NFR-008)");

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

    // NFR-008 (demux): N parallel Subscribe/Dispose cycles against continuous RX traffic must
    // not throw, must not starve a long-lived subscription, and must only ever deliver frames
    // matching each subscription's own filter.
    [Fact]
    public async Task CanBusService_Parallel_Subscribe_Dispose_Under_Traffic_No_Races()
    {
        var session = NewSession();
        using var trafficBus = Open(session, 0);
        using var serviceBus = Open(session, 1);
        using var service = new CanBusService(serviceBus);

        using var cts = new CancellationTokenSource(Bounded);

        // Long-lived control subscription: proves the demux keeps dispatching for the whole run.
        var controlCount = 0;
        using var control = service.Subscribe(_ => true);
        var controlReader = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in control.Frames.WithCancellation(cts.Token))
                {
                    Interlocked.Increment(ref controlCount);
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
        });

        // Continuous two-ID traffic against the service's bus.
        var pump = Task.Run(() =>
        {
            var flip = false;
            while (!cts.IsCancellationRequested)
            {
                trafficBus.Transmit(CanFrame.Classic(flip ? 0x100 : 0x200, new byte[] { 1 }));
                flip = !flip;
            }
        });

        var exceptions = new ConcurrentQueue<Exception>();
        var workers = Enumerable.Range(0, 4).Select(w => Task.Run(async () =>
        {
            try
            {
                var myId = w % 2 == 0 ? 0x100 : 0x200;
                for (var i = 0; i < 25; i++)
                {
                    using var sub = service.Subscribe(f => f.ID == myId);
                    // Drain briefly, then dispose mid-stream (the churn under test).
                    using var readCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
                    try
                    {
                        await foreach (var frame in sub.Frames.WithCancellation(readCts.Token))
                        {
                            frame.ID.Should().Be(myId,
                                "a subscription must only ever observe its own filter's frames");
                        }
                    }
                    catch (OperationCanceledException) { /* expected: our short drain window */ }
                }
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        })).ToArray();

        await Task.WhenAll(workers);
        cts.Cancel();
        try { await pump; } catch { /* transmit-in-flight races shutdown */ }
        try { await controlReader; } catch { /* idem */ }

        exceptions.Should().BeEmpty(
            "parallel Subscribe/Dispose churn under traffic must be race-free (NFR-008)");
        controlCount.Should().BeGreaterThan(0,
            "the long-lived subscription must keep receiving frames throughout the churn (no starvation)");
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
