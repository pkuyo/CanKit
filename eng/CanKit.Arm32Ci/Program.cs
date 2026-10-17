using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CanKit.Arm32Ci;

internal static class Program
{
    internal static readonly CancellationTokenSource Stop = new();
    internal static int SignalExitCode;
    internal static string AssemblyPath => typeof(Program).Assembly.Location;

    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("ARM32 CI requires Linux.");
            return 1;
        }
        // The supervisor stops the worker and its children as one process group.
        using var interrupt = args.FirstOrDefault() != "worker" ? Handle(PosixSignal.SIGINT, 130) : null;
        using var terminate = args.FirstOrDefault() != "worker" ? Handle(PosixSignal.SIGTERM, 143) : null;
        using var hangup = args.FirstOrDefault() != "worker" ? Handle(PosixSignal.SIGHUP, 129) : null;
        try
        {
            return args switch
            {
                ["controller"] => await Controller.Run(),
                ["supervise", var source, var results] => await Device.Supervise(source, results, managed: false),
                ["supervise", var source, var results, "--managed-run"] => await Device.Supervise(source, results, managed: true),
                ["worker", var source, var results, var runner] => await Device.Test(source, results, runner),
                _ => throw new ArgumentException("Usage: controller | supervise <source> <results> [--managed-run]")
            };
        }
        catch (OperationCanceledException) when (Stop.IsCancellationRequested)
        {
            return SignalExitCode;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"ARM32 CI failed: {error.Message}");
            return error is CommandFailed failed ? failed.ExitCode : 1;
        }
    }

    private static PosixSignalRegistration Handle(PosixSignal signal, int code) =>
        PosixSignalRegistration.Create(signal, context =>
        {
            context.Cancel = true;
            if (Interlocked.CompareExchange(ref SignalExitCode, code, 0) == 0)
            {
                Stop.Cancel();
            }
        });

    internal static Process Start(string executable, string[] arguments, bool output = false, string? directory = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = output,
            WorkingDirectory = directory ?? Environment.CurrentDirectory
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new IOException($"Could not start {executable}.");
    }

    internal static async Task<string> Exec(string executable, string[] arguments, bool capture = false,
        string? directory = null, CancellationToken? cancellation = null)
    {
        using var process = Start(executable, arguments, capture, directory);
        process.StandardInput.Close();
        try
        {
            var token = cancellation ?? Stop.Token;
            var output = capture ? process.StandardOutput.ReadToEndAsync(token) : Task.FromResult("");
            await process.WaitForExitAsync(token);
            RequireSuccess(process.ExitCode);
            return await output;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    internal static void RequireSuccess(int status)
    {
        if (status != 0)
        {
            throw new CommandFailed(status);
        }
    }

    internal sealed class CommandFailed(int exitCode, string? message = null)
        : Exception(message ?? $"Command failed with exit code {exitCode}.")
    {
        public int ExitCode { get; } = exitCode;
    }
}
