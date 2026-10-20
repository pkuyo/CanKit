using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CanKit.Arm32Ci;

internal static class Device
{
    internal static async Task<int> Supervise(string source, string results, bool managed)
    {
        source = Path.GetFullPath(source);
        results = Path.GetFullPath(results);
        var home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cache = Path.Combine(home, ".cache", "cankit-arm32");
        var runDirectory = Path.GetDirectoryName(source)!;
        if (managed &&
            (Path.GetDirectoryName(runDirectory) != cache || !Regex.IsMatch(Path.GetFileName(runDirectory), @"\Arun-[a-zA-Z0-9_-]+\z") ||
             Path.GetFileName(source) != "source" || results != Path.Combine(runDirectory, "results")))
        {
            throw new ArgumentException("Managed runs require a dedicated run-* directory under the ARM32 cache.");
        }

        Directory.CreateDirectory(cache);
        var started = Stopwatch.GetTimestamp();
        var heartbeat = started;
        Process? worker = null;
        var preserveReport = false;
        using var deviceLock = LinuxNative.OpenLock(Path.Combine(cache, "device.lock"));
        if (managed)
        {
            // A silent stdin can block this reader; process shutdown must not wait for it.
            _ = Task.Run(() =>
            {
                try
                {
                    using var input = Console.OpenStandardInput();
                    var buffer = new byte[256];
                    while (input.Read(buffer) > 0)
                    {
                        Interlocked.Exchange(ref heartbeat, Stopwatch.GetTimestamp());
                    }
                }
                catch (IOException)
                {
                    // A failed pipe is a disconnect, just like EOF.
                }
                finally
                {
                    Interlocked.Exchange(ref heartbeat, 0);
                }
            });
        }

        void CheckConnection()
        {
            Program.Stop.Token.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started).TotalMinutes >= 40)
            {
                throw new Program.CommandFailed(124, "ARM32 test run exceeded 40 minutes");
            }
            if (managed)
            {
                var lastHeartbeat = Interlocked.Read(ref heartbeat);
                if (lastHeartbeat == 0)
                {
                    throw new Program.CommandFailed(130, "ARM32 controller disconnected");
                }
                if (Stopwatch.GetElapsedTime(lastHeartbeat).TotalSeconds >= 30)
                {
                    throw new Program.CommandFailed(124, "ARM32 controller heartbeat expired");
                }
            }
        }

        try
        {
            while (!LinuxNative.TryLock(deviceLock))
            {
                CheckConnection();
                if (Stopwatch.GetElapsedTime(started).TotalMinutes >= 5)
                {
                    throw new IOException("Another ARM32 test run is still using the vcan interfaces");
                }
                await Task.Delay(100);
            }
            CheckConnection();
            // Process.Start inherits our group; setsid therefore execs without forking.
            worker = Program.Start("setsid", ["dotnet", Program.AssemblyPath, "worker", source, results]);
            worker.StandardInput.Close();
            while (true)
            {
                if (worker.HasExited)
                {
                    preserveReport = true;
                    return worker.ExitCode;
                }
                CheckConnection();
                await Task.Delay(100);
            }
        }
        finally
        {
            if (worker is not null)
            {
                LinuxNative.SignalGroup(worker.Id, 15);
                await Task.Delay(1000);
                // Cancellation may arrive before setsid creates the group. Stop the
                // launcher first so it cannot create a group after the final signal.
                if (!worker.HasExited)
                {
                    worker.Kill();
                }
                await worker.WaitForExitAsync();
                LinuxNative.SignalGroup(worker.Id, 9);
                worker.Dispose();
            }
            if (managed)
            {
                if (preserveReport)
                {
                    Directory.Delete(source, recursive: true);
                    File.WriteAllText(Path.Combine(runDirectory, "finished"), "");
                }
                else
                {
                    Directory.Delete(runDirectory, recursive: true);
                }
            }
            // The lock is disposed only after process and source cleanup.
        }
    }

    internal static async Task<int> Test(string source, string results)
    {
        foreach (var (name, value) in new (string, string)[]
        {
            ("DOTNET_NOLOGO", "true"), ("DOTNET_CLI_TELEMETRY_OPTOUT", "true"),
            ("DOTNET_CLI_UI_LANGUAGE", "en"), ("CANKIT_TEST_ADAPTERS", "CanKit.Adapter.SocketCAN"),
            ("MSBUILDDISABLENODEREUSE", "1"), ("DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER", "1"),
            ("UseSharedCompilation", "false")
        })
        {
            Environment.SetEnvironmentVariable(name, value);
        }
        if (RuntimeInformation.ProcessArchitecture != Architecture.Arm ||
            (await Program.Exec("uname", ["-m"], capture: true)).Trim() != "armv7l" ||
            (await Program.Exec("getconf", ["LONG_BIT"], capture: true)).Trim() != "32")
        {
            throw new IOException("These tests require the native ARM32 kernel, userspace and .NET host.");
        }
        var info = await Program.Exec("dotnet", ["--info"], capture: true, directory: source);
        Console.Write(info);
        await File.WriteAllTextAsync(Path.Combine(results, "dotnet-info.txt"), info);
        foreach (var name in new[] { "vcan0", "vcan1", "vcan2" })
        {
            var details = await Program.Exec("ip", ["-details", "link", "show", name], capture: true);
            if (!Regex.IsMatch(details, @"\bvcan\b"))
            {
                throw new IOException($"{name} must be a vcan interface.");
            }
        }
        await Program.Exec("dotnet", ["restore", "CanKitAdapters.slnf"], directory: source);
        await Program.Exec("dotnet", ["test", "--solution", "CanKitAdapters.slnf", "-c", "Release", "-f", "net8.0", "--no-restore",
            "--results-directory", results, "--report-trx", "--report-trx-filename", "Arm32TestResults.trx"], directory: source);
        return 0;
    }
}
