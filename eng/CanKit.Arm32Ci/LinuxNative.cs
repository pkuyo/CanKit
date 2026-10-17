using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CanKit.Arm32Ci;

// Linux/glibc ABI used by Ubuntu runners and the Raspberry Pi. Keep native details
// here so the controller, supervisor and test flow can be reviewed independently.
internal static class LinuxNative
{
    private const string Libc = "libc";
    private const int Interrupted = 4;
    private const int WouldBlock = 11;

    private static void Check(int result)
    {
        if (result != 0)
        {
            throw new Win32Exception(result);
        }
    }

    internal static SafeFileHandle OpenLock(string path)
    {
        // File.OpenHandle also takes a shared flock, so it cannot open a held lock for polling.
        // O_RDWR | O_CREAT | O_CLOEXEC. Never inherited by a worker.
        var fd = open(path, 2 | 0x40 | 0x80000, 384);
        if (fd < 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return new SafeFileHandle(fd, ownsHandle: true);
    }

    internal static bool TryLock(SafeFileHandle handle)
    {
        if (flock(handle, 2 | 4) == 0) // LOCK_EX | LOCK_NB
        {
            return true;
        }
        var error = Marshal.GetLastPInvokeError();
        if (error is WouldBlock or Interrupted)
        {
            return false;
        }
        throw new Win32Exception(error);
    }

    internal static int SpawnSession(string executable, string[] arguments)
    {
        // glibc spawnattr is 336 bytes; file_actions is 80 on 64-bit, 76 on ARM32.
        // Treat both as opaque storage initialized and accessed only through libc.
        using var attributes = new NativeBuffer(336);
        using var actions = new NativeBuffer(80);
        using var signalMask = new NativeBuffer(128);
        Check(posix_spawnattr_init(attributes.Pointer));
        try
        {
            Check(posix_spawn_file_actions_init(actions.Pointer));
            try
            {
                Check(sigemptyset(signalMask.Pointer));
                Check(posix_spawnattr_setsigmask(attributes.Pointer, signalMask.Pointer));
                Check(posix_spawnattr_setflags(attributes.Pointer, 0x80 | 0x08)); // SETSID | SETSIGMASK
                Check(posix_spawn_file_actions_addopen(actions.Pointer, 0, "/dev/null", 0, 0));
                Check(posix_spawn_file_actions_addclosefrom_np(actions.Pointer, 3));
                string?[] argv = [executable, .. arguments, null];
                string?[] environment = [.. Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                    .Select(entry => $"{entry.Key}={entry.Value}"), null];
                Check(posix_spawnp(out var pid, executable, actions.Pointer, attributes.Pointer, argv, environment));
                return pid;
            }
            finally
            {
                posix_spawn_file_actions_destroy(actions.Pointer);
            }
        }
        finally
        {
            posix_spawnattr_destroy(attributes.Pointer);
        }
    }

    internal static int? PeekExitCode(int pid)
    {
        var info = new byte[128]; // siginfo_t, including its architecture-dependent union alignment.
        if (waitid(1, pid, info, 4 | 1 | 0x01000000) < 0) // P_PID, WEXITED | WNOHANG | WNOWAIT
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == Interrupted)
            {
                return null;
            }
            throw new Win32Exception(error);
        }
        var childOffset = IntPtr.Size == 8 ? 16 : 12;
        if (BitConverter.ToInt32(info, childOffset) == 0)
        {
            return null;
        }
        var status = BitConverter.ToInt32(info, childOffset + 8);
        return BitConverter.ToInt32(info, 8) == 1 ? status : 128 + status; // CLD_EXITED
    }

    internal static void SignalGroup(int pid, int signal)
    {
        if (kill(-pid, signal) != 0 && Marshal.GetLastPInvokeError() != 3) // ESRCH
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    internal static void Reap(int pid)
    {
        while (waitpid(pid, out _, 0) < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != Interrupted)
            {
                throw new Win32Exception(error);
            }
        }
    }

    internal static int ReadHeartbeat()
    {
        var descriptor = new PollDescriptor { FileDescriptor = 0, Events = 1 }; // POLLIN; poll also reports HUP.
        var result = poll(ref descriptor, 1, 0);
        if (result < 0 && Marshal.GetLastPInvokeError() != Interrupted)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        if (result <= 0)
        {
            return -1;
        }
        var bytes = read(0, new byte[4096], 4096).ToInt32();
        if (bytes < 0 && Marshal.GetLastPInvokeError() != Interrupted)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return bytes;
    }

    private sealed class NativeBuffer(int size) : IDisposable
    {
        public nint Pointer { get; } = Marshal.AllocHGlobal(size);
        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollDescriptor
    {
        public int FileDescriptor;
        public short Events;
        public short ReturnedEvents;
    }

    [DllImport(Libc, SetLastError = true)] private static extern int open(string path, int flags, int mode);
    [DllImport(Libc, SetLastError = true)] private static extern int flock(SafeFileHandle fd, int operation);
    [DllImport(Libc)] private static extern int posix_spawnattr_init(nint attributes);
    [DllImport(Libc)] private static extern int posix_spawnattr_destroy(nint attributes);
    [DllImport(Libc)] private static extern int posix_spawnattr_setflags(nint attributes, short flags);
    [DllImport(Libc)] private static extern int posix_spawnattr_setsigmask(nint attributes, nint signalMask);
    [DllImport(Libc)] private static extern int sigemptyset(nint signalMask);
    [DllImport(Libc)] private static extern int posix_spawn_file_actions_init(nint actions);
    [DllImport(Libc)] private static extern int posix_spawn_file_actions_destroy(nint actions);
    [DllImport(Libc)] private static extern int posix_spawn_file_actions_addopen(nint actions, int fd, string path, int flags, int mode);
    [DllImport(Libc)] private static extern int posix_spawn_file_actions_addclosefrom_np(nint actions, int fd);
    [DllImport(Libc)]
    private static extern int posix_spawnp(out int pid, string executable, nint actions, nint attributes,
        [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string?[] arguments,
        [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string?[] environment);
    [DllImport(Libc, SetLastError = true)] private static extern int waitid(int type, int pid, [Out] byte[] info, int options);
    [DllImport(Libc, SetLastError = true)] private static extern int waitpid(int pid, out int status, int options);
    [DllImport(Libc, SetLastError = true)] private static extern int kill(int pid, int signal);
    [DllImport(Libc, SetLastError = true)] private static extern int poll(ref PollDescriptor descriptor, nuint count, int timeout);
    [DllImport(Libc, SetLastError = true)] private static extern nint read(int fd, [Out] byte[] buffer, nuint count);
}
