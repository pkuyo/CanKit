using System.Formats.Tar;
using System.Text.RegularExpressions;

namespace CanKit.Arm32Ci;

internal static class Controller
{
    private static string _target = "";
    private static string _remote = "";

    internal static async Task<int> Run()
    {
        static string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) ?? fallback;
        var host = Env("ARM32_HOST", "");
        var user = Env("ARM32_USER", "actions-runner");
        var key = $"run-{Env("GITHUB_RUN_ID", "local")}-{Env("GITHUB_RUN_ATTEMPT", "1")}-{Env("GITHUB_JOB", "manual")}-{Environment.ProcessId}";
        if (!Regex.IsMatch(host, @"\A[a-zA-Z0-9][a-zA-Z0-9.-]*\z") ||
            !Regex.IsMatch(user, @"\A[a-z_][a-zA-Z0-9_-]*\z") || !Regex.IsMatch(key, @"\Arun-[a-zA-Z0-9_-]+\z"))
        {
            throw new ArgumentException("Invalid ARM32_HOST, ARM32_USER or run identifier.");
        }
        _target = $"{user}@{host}";
        _remote = $".cache/cankit-arm32/{key}";
        var source = Env("ARM32_SOURCE_DIRECTORY", ".");
        var results = Path.Combine(Env("GITHUB_WORKSPACE", Environment.CurrentDirectory), "TestResults", "arm32");
        Directory.CreateDirectory(results);
        var scratch = Directory.CreateTempSubdirectory("cankit-arm32-").FullName;
        var prepared = false;
        var testsStarted = false;
        var status = 1;
        try
        {
            var commit = await Program.Exec("git", ["-C", source, "rev-parse", "HEAD"], capture: true);
            Console.Write(commit);
            await File.WriteAllTextAsync(Path.Combine(results, "commit.txt"), commit);
            var sourceArchive = Path.Combine(scratch, "source.tar");
            await Program.Exec("git", ["-C", source, "archive", "--format=tar", $"--output={sourceArchive}", "HEAD"]);
            var toolArchive = Path.Combine(scratch, "tool.tar");
            TarFile.CreateFromDirectory(AppContext.BaseDirectory, toolArchive, includeBaseDirectory: false);

            prepared = true;
            await Remote($"mkdir -p -- '{_remote}/source' '{_remote}/results' '{_remote}/tool'");
            await Upload(sourceArchive, "source");
            await Upload(toolArchive, "tool");
            testsStarted = true;
            status = await RunTests(Path.Combine(results, "remote.log"));
        }
        catch (OperationCanceledException) when (Program.Stop.IsCancellationRequested)
        {
            status = Program.SignalExitCode;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"::error::ARM32 run failed: {error.Message}");
            status = error is Program.CommandFailed failed ? failed.ExitCode : 1;
        }
        finally
        {
            try
            {
                if (testsStarted && !Program.Stop.IsCancellationRequested)
                {
                    try
                    {
                        await DownloadReport(scratch, results);
                    }
                    catch (Exception error)
                    {
                        Console.Error.WriteLine($"::error::The ARM32 test report could not be retrieved: {error.Message}");
                        if (status == 0)
                        {
                            status = 1;
                        }
                    }
                }
                if (prepared)
                {
                    await Cleanup();
                }
            }
            finally
            {
                Directory.Delete(scratch, recursive: true);
            }
        }
        return Program.Stop.IsCancellationRequested ? Program.SignalExitCode : status;
    }

    private static string[] SshArguments(string command, int seconds) =>
        ["--kill-after=1s", $"{seconds}s", "tailscale", "ssh", _target, "cd && " + command];

    private static Task<string> Remote(string command, int seconds = 60, CancellationToken? cancellation = null) =>
        Program.Exec("timeout", SshArguments(command, seconds), cancellation: cancellation);

    private static async Task Upload(string archive, string destination)
    {
        using var ssh = Program.Start("timeout", SshArguments($"tar -xf - -C '{_remote}/{destination}'", 300));
        try
        {
            await using var input = File.OpenRead(archive);
            await input.CopyToAsync(ssh.StandardInput.BaseStream, Program.Stop.Token);
            ssh.StandardInput.Close();
            await ssh.WaitForExitAsync(Program.Stop.Token);
            Program.RequireSuccess(ssh.ExitCode);
        }
        finally
        {
            if (!ssh.HasExited)
            {
                ssh.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task<int> RunTests(string logPath)
    {
        var command = $"setsid --fork --wait bash '{_remote}/tool/Start-Arm32Ci.sh' " +
            $"supervise '{_remote}/source' '{_remote}/results' --managed-run 2>&1";
        using var ssh = Program.Start("timeout", SshArguments(command, 41 * 60), output: true);
        await using var log = File.Create(logPath);
        using var stopLogging = new CancellationTokenSource();
        var output = CopyLog(ssh.StandardOutput.BaseStream, log, stopLogging.Token);
        var exited = ssh.WaitForExitAsync();
        try
        {
            while (!exited.IsCompleted)
            {
                Program.Stop.Token.ThrowIfCancellationRequested();
                if (output.IsFaulted)
                {
                    await output;
                }
                try
                {
                    await ssh.StandardInput.WriteAsync(".\n".AsMemory(), Program.Stop.Token);
                }
                catch (IOException)
                {
                    break; // Remote completion may close stdin between heartbeats.
                }
                await Task.WhenAny(exited, Task.Delay(2000, Program.Stop.Token));
            }
            await exited.WaitAsync(Program.Stop.Token);
            await output.WaitAsync(Program.Stop.Token);
            return ssh.ExitCode;
        }
        finally
        {
            try
            {
                // Closing stdin tells the detached supervisor to stop this run.
                ssh.StandardInput.Close();
                try
                {
                    await exited.WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch (TimeoutException)
                {
                    ssh.Kill(entireProcessTree: true);
                }
            }
            finally
            {
                // Drain shutdown output before disposing the log, but bound a stalled stream.
                stopLogging.CancelAfter(TimeSpan.FromSeconds(1));
                try
                {
                    await output;
                }
                catch (OperationCanceledException) when (stopLogging.IsCancellationRequested)
                {
                }
            }
        }
    }

    private static async Task CopyLog(Stream input, Stream file, CancellationToken cancellation)
    {
        var console = Console.OpenStandardOutput();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellation)) != 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, count), cancellation);
            await console.WriteAsync(buffer.AsMemory(0, count), cancellation).AsTask().WaitAsync(cancellation);
        }
    }

    private static async Task DownloadReport(string scratch, string results)
    {
        var path = Path.Combine(scratch, "Arm32TestResults.trx");
        using var ssh = Program.Start("timeout", SshArguments($"cat '{_remote}/results/Arm32TestResults.trx'", 60), output: true);
        ssh.StandardInput.Close();
        try
        {
            await using (var file = File.Create(path))
            {
                await ssh.StandardOutput.BaseStream.CopyToAsync(file, Program.Stop.Token);
            }
            await ssh.WaitForExitAsync(Program.Stop.Token);
            Program.RequireSuccess(ssh.ExitCode);
            if (new FileInfo(path).Length == 0)
            {
                throw new IOException("The ARM32 test report is empty.");
            }
            File.Copy(path, Path.Combine(results, "Arm32TestResults.trx"), overwrite: true);
        }
        finally
        {
            if (!ssh.HasExited)
            {
                ssh.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task Cleanup()
    {
        try
        {
            // Also works when upload failed before the C# tool reached the device.
            var command = $"if [ ! -d '{_remote}' ]; then :; " +
                $"elif [ -f '{_remote}/finished' ]; then rm -rf -- '{_remote}'; " +
                $"else flock -n .cache/cankit-arm32/device.lock rm -rf -- '{_remote}'; fi";
            await Remote(command, Program.Stop.IsCancellationRequested ? 2 : 60,
                Program.Stop.IsCancellationRequested ? CancellationToken.None : Program.Stop.Token);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"::warning::Remote workspace retained; the test machine may still be busy. {error.Message}");
        }
    }
}
