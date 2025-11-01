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
