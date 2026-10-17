using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
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
        if (managed)
        {
            if (Path.GetDirectoryName(runDirectory) != cache || !Regex.IsMatch(Path.GetFileName(runDirectory), @"\Arun-[a-zA-Z0-9_-]+\z") ||
                Path.GetFileName(source) != "source" || results != Path.Combine(runDirectory, "results"))
            {
                throw new ArgumentException("Managed runs require a dedicated run-* directory under the ARM32 cache.");
            }
            foreach (var path in new[] { Path.GetDirectoryName(cache)!, cache, runDirectory, source, results })
            {
                if (new FileInfo(path).LinkTarget is not null)
                {
                    throw new IOException($"ARM32 run paths must not be symbolic links: {path}");
                }
            }
        }

        Directory.CreateDirectory(cache);
        var started = Stopwatch.GetTimestamp();
        var heartbeat = started;
        var child = 0;
        var preserveReport = false;
        using var deviceLock = LinuxNative.OpenLock(Path.Combine(cache, "device.lock"));

        void CheckConnection()
        {
            Program.Stop.Token.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started).TotalMinutes >= 40)
            {
                throw new Program.CommandFailed(124, "ARM32 test run exceeded 40 minutes");
            }
            if (managed)
            {
                var bytes = LinuxNative.ReadHeartbeat();
                if (bytes == 0)
                {
                    throw new Program.CommandFailed(130, "ARM32 controller disconnected");
                }
                if (bytes > 0)
                {
                    heartbeat = Stopwatch.GetTimestamp();
                }
                if (Stopwatch.GetElapsedTime(heartbeat).TotalSeconds >= 30)
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
            var runner = ReadRunner(source);
            child = LinuxNative.SpawnSession("dotnet", [Program.AssemblyPath, "worker", source, results, runner]);
            while (true)
            {
                if (LinuxNative.PeekExitCode(child) is { } exitCode)
                {
                    preserveReport = true;
                    return exitCode;
                }
                CheckConnection();
                await Task.Delay(100);
            }
        }
        finally
        {
            // Keep the leader unreaped (WNOWAIT) until group cleanup is complete:
            // its PID cannot be reused for an unrelated group between these calls.
            if (child != 0)
            {
                LinuxNative.SignalGroup(child, 15);
                await Task.Delay(1000);
                LinuxNative.SignalGroup(child, 9);
                LinuxNative.Reap(child);
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

    private static string ReadRunner(string source)
    {
        var path = Path.Combine(source, "global.json");
        if (!File.Exists(path))
        {
            return "VSTest";
        }
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            var root = config.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("the root must be an object");
            }
            if (!root.TryGetProperty("test", out var test))
            {
                return "VSTest";
            }
            if (test.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("test must be an object");
            }
            if (!test.TryGetProperty("runner", out var runner))
            {
                return "VSTest";
            }
            if (runner.ValueKind != JsonValueKind.String || runner.GetString() is not ("VSTest" or "Microsoft.Testing.Platform"))
            {
                throw new JsonException("unsupported test.runner");
            }
            return runner.GetString()!;
        }
        catch (JsonException error)
        {
            throw new IOException($"Invalid global.json: {error.Message}", error);
        }
    }

    internal static async Task<int> Test(string source, string results, string runner)
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
        if (!Regex.IsMatch(info, @"^\s*Architecture:\s+arm\s*$", RegexOptions.Multiline))
        {
            throw new IOException("The .NET host must use ARM32.");
        }
        foreach (var name in new[] { "vcan0", "vcan1", "vcan2" })
        {
            var details = await Program.Exec("ip", ["-details", "link", "show", name], capture: true);
            if (!Regex.IsMatch(details, @"\bvcan\b"))
            {
                throw new IOException($"{name} must be a vcan interface.");
            }
        }
        await Program.Exec("dotnet", ["restore", "CanKitAdapters.slnf"], directory: source);
        string[] arguments = runner == "Microsoft.Testing.Platform"
            ? ["test", "--solution", "CanKitAdapters.slnf", "-c", "Release", "-f", "net8.0", "--no-restore",
                "--results-directory", results, "--report-trx", "--report-trx-filename", "Arm32TestResults.trx"]
            : ["test", "CanKitAdapters.slnf", "-c", "Release", "-f", "net8.0", "--no-restore",
                "--results-directory", results, "--logger", "trx;LogFileName=Arm32TestResults.trx"];
        await Program.Exec("dotnet", arguments, directory: source);
        return 0;
    }
}
