using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CanKit.Abstractions.SPI.Tests;
using CanKit.Tests.Utils;

namespace CanKit.Tests;

public class TestCaseProvider : IDisposable
{
    static TestCaseProvider()
    {
        Provider = new EmptyTestDataProvider();

        // Some test suites (e.g. VirtualBusOwnershipTests, RawCanSubscriptionTests) always open
        // "virtual://" endpoints to exercise adapter-agnostic behavior, regardless of which vendor
        // adapter is under test in this CI job. CanRegistry only discovers endpoint/factory
        // registrations from assemblies that are actually loaded into the AppDomain by the time its
        // lazy singleton is first built, so CanKit.Adapter.Virtual must be force-loaded here too -
        // otherwise those tests fail with "No endpoint handler registered for 'virtual://...'" on
        // every CI job other than the Virtual one.
        SafeLoad(new AssemblyName("CanKit.Adapter.Virtual"));

        var env = Environment.GetEnvironmentVariable("CANKIT_TEST_ADAPTERS");
        if (env is null)
        {
            Console.WriteLine($"No environment variable found. Skipping all tests.");
            return;
        }

        if (SafeLoad(new AssemblyName(env)))
        {
            try
            {
                var type = Type.GetType($"{env}.Tests.TestDataProvider, {env}", true, true);
                Provider = (ITestDataProvider)Activator.CreateInstance(type!, [])!;
            }
            catch
            {
                Console.WriteLine($"Tried to instantiate test data provider failed, AssemblyName:{env}");
                Provider = new EmptyTestDataProvider();
            }
            AbitRate = Provider.BaudRate?.aBit ?? 1_000_000;
            DbitRate = Provider.BaudRate?.dBit ?? 8_000_000;
        }
    }

    public static int AbitRate { get; }

    public static int DbitRate { get; }

    public static ITestDataProvider Provider { get; }

    public static Random Rand { get; } = new Random();

    private static bool SafeLoad(AssemblyName path)
    {
        try
        {
#if NET5_0_OR_GREATER
            var asm = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyName(path);
#else
            var asm = Assembly.Load(path); // .NET Framework
#endif

        }
        catch
        {
            return false;
        }

        return true;
    }
    public void Dispose()
    {
    }
}
