using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CanKit.Tests;

public class TestAssemblyLoader : IDisposable
{
    public TestAssemblyLoader()
    {
        var workDir = AppContext.BaseDirectory;

        foreach (var name in Directory.EnumerateFiles(workDir, "CanKit.Adapter.*.dll", SearchOption.TopDirectoryOnly)
                     .Select(Path.GetFileNameWithoutExtension)
                     .Distinct()
                     .OrderBy(n => n)
                     .Select(i => new AssemblyName(i!)))
        {
            SafeLoad(name);
        }
    }

    private static void SafeLoad(AssemblyName path)
    {
        try
        {
#if NET5_0_OR_GREATER
            var asm = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyName(path);
#else
            var asm = Assembly.Load(path); // .NET Framework
#endif

        }
        catch {/* ignore any preload reflection errors */ }
    }
    public void Dispose()
    {
    }
}
