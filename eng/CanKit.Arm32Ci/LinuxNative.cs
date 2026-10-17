using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CanKit.Arm32Ci;

internal static class LinuxNative
{
    private const string Libc = "libc";
    private const int Interrupted = 4;
    private const int WouldBlock = 11;

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

    internal static void SignalGroup(int pid, int signal)
    {
        if (kill(-pid, signal) != 0 && Marshal.GetLastPInvokeError() != 3) // ESRCH
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [DllImport(Libc, SetLastError = true)] private static extern int open(string path, int flags, int mode);
    [DllImport(Libc, SetLastError = true)] private static extern int flock(SafeFileHandle fd, int operation);
    [DllImport(Libc, SetLastError = true)] private static extern int kill(int pid, int signal);
}
